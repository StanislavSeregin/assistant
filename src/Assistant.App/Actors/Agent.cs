using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using Proto;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Metadata(string Name, string Description, string Instructions, bool IsMaster);

    public class Actor(
        ChatClientFactory chatClientFactory,
        AgentConcurrencyLimiter concurrencyLimiter,
        IOptions<Settings> settings) : IActor
    {
        private record Participants(string Name, string Description);

        private sealed class MessageDraft(string to, bool waitForReply, Guid streamId)
        {
            public string To { get; } = to;

            public bool WaitForReply { get; } = waitForReply;

            public Guid StreamId { get; } = streamId;

            public StringBuilder Content { get; } = new();
        }

        private readonly TimeSpan _replyTimeout = settings.Value.ReplyTimeout;

        private const int MaxSilentNudges = 1;

        /// <summary>
        /// Standalone line separating scratch notes from the letter body (mailbox strips prefix through this line).
        /// </summary>
        private const string LetterBreak = "<<<LETTER>>>";

        private MessageDraft? _draft;

        private Guid? _thinkingStreamId;

        private int _deliveriesThisRun;

        private Metadata Metadata
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        private ChatClientAgent Agent
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        private AgentSession Session
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        private IContext Context
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        public async Task ReceiveAsync(IContext context)
        {
            Context = context;
            await (context.Message switch
            {
                Metadata msg => Init(msg),
                AgentRegistry.ReceivedMessages msg => HandleMessages(msg),
                _ => Task.CompletedTask
            });
        }

        private async Task Init(Metadata msg)
        {
            Metadata = msg;

            Agent = chatClientFactory.GetChatClient().AsAIAgent(
                name: Metadata.Name,
                description: Metadata.Description,
                instructions: BuildInstructions(),
                tools: BuildTools());

            Session = await Agent.CreateSessionAsync(Context.CancellationToken);
            Ready();
        }

        private string BuildInstructions()
        {
            var instructions = $"""
                You are {Metadata.Name}. {Metadata.Description}

                {Metadata.Instructions}

                Messaging:
                1. Call {nameof(StartMessage)}(to, waitForReply?) to open a channel.
                2. Write free text, then call {nameof(SendMessage)}() (open channels also deliver at turn end).
                3. Start the letter block with a line that is exactly `{LetterBreak}` (own line, newlines around it).
                   Everything below that line is the letter. Never put `{LetterBreak}` at the end.

                Rules:
                - Free text before {nameof(StartMessage)} / after {nameof(SendMessage)} is internal thinking.
                - Do not call {nameof(StartMessage)} while a message is already open — {nameof(SendMessage)} first.
                - Reply to whoever assigned you the work with your results (one Start→text→Send).
                - Only message User directly when you are the coordinator, or when explicitly asked to inform User.
                - Use {nameof(GetParticipants)} before messaging someone new.
                - Never claim work is done until {nameof(SendMessage)} returned success.
                - After a waitForReply returns, you must usually send a follow-up (e.g. results to User or assigner).
                - Never tell others that a teammate finished unless you received their reply.
                - When you need someone's answer to continue, set waitForReply=true on {nameof(StartMessage)}.
                - Be brief and practical.
                """;

            if (!Metadata.IsMaster)
            {
                return instructions;
            }

            return instructions + $"""

                You are the coordinator. User is the task assigner.
                Use {nameof(CreateNewParticipant)} to add specialists when needed — wait for its success before messaging them.
                For dependent work, assign sequentially with waitForReply=true; fan out only independent tasks.
                Pass decisions and artifacts between specialists via messages — do not assume others already know what was decided.
                Never summarize results only in thinking — Start→text→Send them to User.
                """;
        }

        private AITool[] BuildTools()
        {
            AITool[] common =
            [
                AIFunctionFactory.Create(StartMessage),
                AIFunctionFactory.Create(SendMessage),
                AIFunctionFactory.Create(GetParticipants)
            ];

            return Metadata.IsMaster
                ? [.. common, AIFunctionFactory.Create(CreateNewParticipant)]
                : common;
        }

        private void Ready()
        {
            var pid = Context.Parent ?? throw new InvalidOperationException();
            Context.Send(pid, new MessageEnvelope(new AgentRegistry.Ready(Metadata.Name), Context.Self));
        }

        private async Task HandleMessages(AgentRegistry.ReceivedMessages msg)
        {
            try
            {
                var content = string.Join(Environment.NewLine, msg.Messages.Select(m => $"[{m.From}]: {m.Content}"));
                var who = string.Join(", ", msg.Messages.Select(m => m.From).Distinct());

                var delivered = await RunAgent(new Microsoft.Extensions.AI.ChatMessage
                {
                    Role = ChatRole.User,
                    Contents = [new TextContent(content)]
                });

                for (var nudge = 0; !delivered && nudge < MaxSilentNudges; nudge++)
                {
                    var alert =
                        $"""
                        You finished without a follow-up delivery. {who} may still be waiting for a result.
                        If you already got a teammate's reply, forward it (StartMessage → `{LetterBreak}` + letter → SendMessage).
                        Otherwise answer the open request the same way.
                        """;

                    Context.System.EventStream.Publish(new Observation.NudgeLogged(Metadata.Name, alert.Trim()));

                    delivered = await RunAgent(new Microsoft.Extensions.AI.ChatMessage
                    {
                        Role = ChatRole.User,
                        Contents =
                        [
                            new TextContent(
                                $"""
                                [Alert] {alert.Trim()}
                                After {nameof(StartMessage)}, write ONLY the letter as {Metadata.Name} — no planning aloud.
                                """)
                        ]
                    });
                }

                if (!delivered)
                {
                    Context.System.EventStream.Publish(new Observation.ErrorLogged(
                        Metadata.Name,
                        "Turn ended without delivering a message."));
                }
            }
            catch (Exception ex)
            {
                Context.System.EventStream.Publish(new Observation.ErrorLogged(
                    Metadata.Name,
                    $"Error: {ex.Message}"));
            }
            finally
            {
                Ready();
            }
        }

        private Task<bool> RunAgent(Microsoft.Extensions.AI.ChatMessage chatMessage) =>
            concurrencyLimiter.RunAsync(async () =>
            {
                long? inputTokens = null;
                _draft = null;
                _thinkingStreamId = null;
                _deliveriesThisRun = 0;

                try
                {
                    var updates = new List<AgentResponseUpdate>();
                    await foreach (var update in Agent.RunStreamingAsync(
                        chatMessage,
                        Session,
                        chatClientFactory.CreateRunOptions(),
                        Context.CancellationToken))
                    {
                        updates.Add(update);
                        inputTokens = ReadInputTokens(update) ?? inputTokens;

                        var text = ReadVisibleText(update);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        if (_draft is { } draft)
                        {
                            draft.Content.Append(text);
                            Context.System.EventStream.Publish(new Observation.OutboundDelta(draft.StreamId, text));
                        }
                        else
                        {
                            EmitThinking(text);
                        }
                    }

                    inputTokens ??= updates.ToAgentResponse().Usage?.InputTokenCount;
                }
                finally
                {
                    var usageOnOutbound = await FinalizeOpenDraftAsync(inputTokens);
                    CompleteThinking(usageOnOutbound ? null : inputTokens);
                }

                return _deliveriesThisRun > 0;
            }, Context.CancellationToken);

        private void EmitThinking(string text)
        {
            if (_thinkingStreamId is null)
            {
                _thinkingStreamId = Guid.NewGuid();
                Context.System.EventStream.Publish(new Observation.ThinkingStarted(
                    Metadata.Name,
                    _thinkingStreamId.Value));
            }

            Context.System.EventStream.Publish(new Observation.ThinkingDelta(
                Metadata.Name,
                _thinkingStreamId.Value,
                text));
        }

        private void CompleteThinking(long? inputTokens = null)
        {
            if (_thinkingStreamId is not { } streamId)
            {
                return;
            }

            Context.System.EventStream.Publish(new Observation.ThinkingCompleted(
                Metadata.Name,
                streamId,
                inputTokens));
            _thinkingStreamId = null;
        }

        /// <summary>
        /// Dispose-style commit: open draft with a body is delivered at turn end.
        /// Empty drafts are closed without mailbox delivery.
        /// </summary>
        /// <returns>True if an outbound stream was closed (usage should attach there, not to thinking).</returns>
        private async Task<bool> FinalizeOpenDraftAsync(long? inputTokens)
        {
            if (_draft is not { } draft)
            {
                return false;
            }

            var raw = draft.Content.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                Context.System.EventStream.Publish(new Observation.OutboundCompleted(
                    draft.StreamId,
                    inputTokens));
                _draft = null;
                return true;
            }

            var letter = ExtractLetterBody(raw);
            if (string.IsNullOrWhiteSpace(letter))
            {
                Context.System.EventStream.Publish(new Observation.OutboundCompleted(
                    draft.StreamId,
                    inputTokens));
                _draft = null;
                return true;
            }

            await DeliverDraftAsync(draft, letter, inputTokens);
            return true;
        }

        private static long? ReadInputTokens(AgentResponseUpdate update)
        {
            foreach (var content in update.Contents)
            {
                if (content is UsageContent { Details.InputTokenCount: long tokens })
                {
                    return tokens;
                }
            }

            return null;
        }

        private static string ReadVisibleText(AgentResponseUpdate update)
        {
            if (update.Contents is not { Count: > 0 })
            {
                return update.Text;
            }

            var parts = update.Contents
                .Select(content => content switch
                {
                    TextContent { Text: { Length: > 0 } text } => text,
                    TextReasoningContent { Text: { Length: > 0 } text } => text,
                    _ => null
                })
                .Where(text => text is not null);

            var combined = string.Concat(parts);
            return combined.Length > 0 ? combined : update.Text;
        }

        [Description("Open a message to a participant or User. After this, write the letter as free text, then call SendMessage.")]
        private async Task<string> StartMessage(
            [Description("Recipient")] string to,
            [Description("If true, SendMessage will block until the recipient replies (not valid for User)")] bool waitForReply = false)
        {
            if (_draft is not null)
            {
                return $"Already composing a message to '{_draft.To}'. Call {nameof(SendMessage)} first.";
            }

            var participants = await GetParticipantsAsync();
            if (participants.All(p => p.Name != to))
            {
                return $"'{to}' recipient not found. Use `{nameof(GetParticipants)}` for getting participants.";
            }

            waitForReply = waitForReply && to != "User";

            CompleteThinking();

            var streamId = Guid.NewGuid();
            _draft = new MessageDraft(to, waitForReply, streamId);

            Context.System.EventStream.Publish(new Observation.OutboundStarted(
                Metadata.Name,
                to,
                streamId,
                IsForUser: to == "User"));

            return $"Channel open to '{to}'. Next: newline, `{LetterBreak}`, newline, then the letter. Then {nameof(SendMessage)}.";
        }

        [Description("Deliver the message opened with StartMessage. Nothing is sent until this is called.")]
        private async Task<string> SendMessage()
        {
            if (_draft is not { } draft)
            {
                return $"No open message. Call {nameof(StartMessage)} first, write free text, then {nameof(SendMessage)}.";
            }

            var raw = draft.Content.ToString();
            if (string.IsNullOrWhiteSpace(raw))
            {
                return $"Message is empty. Write free text (with `{LetterBreak}` then the letter), then call {nameof(SendMessage)} again.";
            }

            var letter = ExtractLetterBody(raw);
            if (string.IsNullOrWhiteSpace(letter))
            {
                return $"Nothing after `{LetterBreak}`. Write the letter below that line, then call {nameof(SendMessage)} again.";
            }

            return await DeliverDraftAsync(draft, letter);
        }

        /// <summary>
        /// UI keeps the full draft stream; mailbox only gets the body after the letter break.
        /// Supports a dedicated line or an inline marker when the model omits newlines.
        /// </summary>
        internal static string ExtractLetterBody(string content)
        {
            var normalized = content.ReplaceLineEndings("\n");
            var lines = normalized.Split('\n');

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                if (IsLetterBreakLine(trimmed))
                {
                    return ChooseLetterBody(
                        string.Join('\n', lines.AsSpan(0, i)).Trim(),
                        string.Join('\n', lines.AsSpan(i + 1)).Trim());
                }

                if (TrySplitInlineBreak(lines[i], out var beforeInline, out var afterInline))
                {
                    var before = JoinSegments(string.Join('\n', lines.AsSpan(0, i)), beforeInline);
                    var after = JoinSegments(afterInline, string.Join('\n', lines.AsSpan(i + 1)));
                    return ChooseLetterBody(before, after);
                }
            }

            return content.Trim();
        }

        private static bool IsLetterBreakLine(string trimmed) =>
            trimmed == LetterBreak;

        private static bool TrySplitInlineBreak(string line, out string before, out string after)
        {
            var idx = line.IndexOf(LetterBreak, StringComparison.Ordinal);
            if (idx < 0)
            {
                before = after = string.Empty;
                return false;
            }

            before = line[..idx].Trim();
            after = line[(idx + LetterBreak.Length)..].Trim();
            return true;
        }

        private static string JoinSegments(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left))
            {
                return right.Trim();
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return left.Trim();
            }

            return $"{left.TrimEnd()}\n{right.TrimStart()}";
        }

        private static string ChooseLetterBody(string before, string after)
        {
            // Footer-style break: prefer the longer preamble as the letter.
            if (after.Length == 0 || (before.Length > 0 && after.Length < 40 && before.Length >= after.Length))
            {
                return before;
            }

            return after;
        }

        private async Task<string> DeliverDraftAsync(
            MessageDraft draft,
            string mailboxContent,
            long? inputTokens = null)
        {
            var to = draft.To;
            var waitForReply = draft.WaitForReply;
            var streamId = draft.StreamId;
            _draft = null;

            Context.System.EventStream.Publish(new Observation.OutboundCompleted(streamId, inputTokens));
            _deliveriesThisRun++;

            var pid = Context.Parent ?? throw new InvalidOperationException();
            var payload = new AgentRegistry.Message(Metadata.Name, to, mailboxContent, waitForReply);

            if (!waitForReply)
            {
                var sent = await RequestAsync<AgentRegistry.SendMessageResponse>(pid, payload);
                return sent.Result;
            }

            try
            {
                var response = await concurrencyLimiter.WhileReleasedAsync(
                    () => RequestAsync<AgentRegistry.SendMessageResponse>(pid, payload));

                // A wait consumed this delivery for coordination — require a follow-up send
                // (e.g. forward results to User) before the turn counts as complete.
                _deliveriesThisRun = 0;

                return response.Ok
                    ? $"Reply from {to}:{Environment.NewLine}{response.Result}"
                    : response.Result;
            }
            catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
            {
                Context.Send(pid, new MessageEnvelope(
                    new AgentRegistry.CancelReplyWait(Metadata.Name, to),
                    Context.Self));
                _deliveriesThisRun = 0;
                return $"Timed out waiting for reply from '{to}'.";
            }
        }

        [Description("Get participants for messaging")]
        private Task<Participants[]> GetParticipants() => GetParticipantsAsync();

        private async Task<Participants[]> GetParticipantsAsync()
        {
            if (Context.Parent is not { } pid)
            {
                return [];
            }

            var response = await RequestAsync<AgentRegistry.AgentsResponse>(
                pid, new AgentRegistry.AgentsRequest());

            var participants = response.Agents
                .Where(a => a.Name != Metadata.Name)
                .Select(a => new Participants(a.Name, a.Description))
                .ToList();

            if (Metadata.IsMaster)
            {
                participants.Insert(0, new Participants("User", "Director"));
            }

            return [.. participants];
        }

        [Description("Bring in a new participant for collaborative work. Waits until the participant is ready.")]
        private async Task<string> CreateNewParticipant(
            [Description("Role of the new participant (e.g.: MobileDeveloper, Tester, Analyst)")] string name,
            [Description("What this participant will do, their responsibilities and specialization (very briefly)")] string description,
            [Description("Detailed instructions for the new participant: what to do, how to work, expected results")] string instructions)
        {
            var pid = Context.Parent ?? throw new InvalidOperationException();

            CompleteThinking();

            User.PublishToolCall(Context, Metadata.Name, nameof(CreateNewParticipant),
                ("name", name),
                ("description", description),
                ("instructions", instructions));

            var response = await concurrencyLimiter.WhileReleasedAsync(
                () => RequestAsync<AgentRegistry.CreateParticipantResponse>(
                    pid, new Metadata(name, description, instructions, IsMaster: false)));

            return response.Ok ? response.Result : $"Failed: {response.Result}";
        }

        private async Task<T> RequestAsync<T>(PID pid, object payload)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Context.CancellationToken);
            timeout.CancelAfter(_replyTimeout);
            return await Context.RequestAsync<T>(pid, payload, timeout.Token);
        }
    }
}
