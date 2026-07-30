using System.Linq;
using Proto;
using Proto.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public static void PublishToolCall(IContext context, string agent, string toolName, params (string Key, string? Value)[] args)
    {
        var payload = string.Join(
            Environment.NewLine,
            args.Select(arg => $"{arg.Key}: {arg.Value ?? string.Empty}"));

        context.System.EventStream.Publish(new Observation.ToolCallLogged(agent, toolName, payload));
    }

    public record PromptInput;

    public class Actor : IActor
    {
        private const string AgentName = "Secretary";

        private const string AgentInstructions = """
            Coordinate the team. Talk to User directly.
            Delegate specialist work when needed. Be brief.
            Prefer creating an analyst (or equivalent) before implementation roles when requirements are unclear.
            Pass decisions between specialists via StartMessage → free text → SendMessage.
            """;

        private readonly SemaphoreSlim _lock = new(1);

        private readonly Dictionary<Guid, ActiveStream> _streams = [];

        private readonly List<EventStreamSubscription<object>> _subscriptions = [];

        private PID _registryPid = null!;

        private sealed class ActiveStream(
            string? agent,
            DateTime startedAt,
            bool isThinking,
            string? to = null,
            bool isForUser = false)
        {
            public string? Agent { get; } = agent;

            public DateTime StartedAt { get; } = startedAt;

            public bool IsThinking { get; } = isThinking;

            public string? To { get; } = to;

            public bool IsForUser { get; } = isForUser;

            public bool HasOpenStyle { get; set; }
        }

        public Task ReceiveAsync(IContext context) =>
            context.Message switch
            {
                Started => Init(context),
                PromptInput => HandleAsk(context),
                Stopped => Unsubscribe(),
                _ => Task.CompletedTask
            };

        private async Task Init(IContext context)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Clear();
            AnsiConsole.Write(new FigletText("Assistant")
            {
                Justification = Justify.Center,
                Color = ConsoleColor.Cyan
            });

            _subscriptions.Add(context.System.EventStream.Subscribe<Observation.IEvent>(evt =>
            {
                _lock.Wait();
                try
                {
                    Render(evt);
                }
                finally
                {
                    _lock.Release();
                }
            }));

            _registryPid = SpawnRegistry(context);
            await HandleAsk(context);
        }

        private Task Unsubscribe()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Unsubscribe();
            }

            return Task.CompletedTask;
        }

        private static PID SpawnRegistry(IContext context)
        {
            var pid = context.Spawn(context.System.DI().PropsFor<AgentRegistry.Actor>());
            context.Send(pid, new MessageEnvelope(
                new Agent.Metadata(AgentName, "Manager", AgentInstructions, IsMaster: true),
                context.Self));
            return pid;
        }

        private async Task HandleAsk(IContext context)
        {
            await _lock.WaitAsync();
            try
            {
                AnsiConsole.WriteLine();
                var input = AnsiConsole.Prompt(new TextPrompt<string>(">").AllowEmpty());
                if (string.IsNullOrWhiteSpace(input))
                {
                    context.Forward(context.Self);
                    return;
                }

                context.Send(_registryPid, new MessageEnvelope(
                    new AgentRegistry.Message("User", AgentName, input),
                    context.Self));
            }
            finally
            {
                _lock.Release();
            }
        }

        private void Render(Observation.IEvent evt)
        {
            switch (evt)
            {
                case Observation.ThinkingStarted e:
                    OpenStream(e.StreamId, e.Agent, isThinking: true);
                    break;

                case Observation.ThinkingDelta e:
                    WriteDelta(e.StreamId, e.Agent, e.Text, isThinking: true);
                    break;

                case Observation.ThinkingCompleted e:
                    CompleteStream(e.StreamId, e.InputTokens);
                    break;

                case Observation.OutboundStarted e:
                    PauseThinking(e.From);
                    OpenStream(e.StreamId, e.From, isThinking: false, e.To, e.IsForUser);
                    break;

                case Observation.OutboundDelta e:
                    WriteDelta(e.StreamId, agent: null, e.Text, isThinking: false);
                    break;

                case Observation.OutboundCompleted e:
                    CompleteStream(e.StreamId, e.InputTokens);
                    break;

                case Observation.ToolCallLogged e:
                    PauseThinking(e.Agent);
                    WriteToolCall(e);
                    break;

                case Observation.NudgeLogged e:
                    PauseThinking(e.Agent);
                    WriteNudge(e);
                    break;

                case Observation.ErrorLogged e:
                    PauseThinking(e.Agent);
                    WriteError(e);
                    break;
            }
        }

        private void WriteDelta(Guid streamId, string? agent, string text, bool isThinking)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (!_streams.TryGetValue(streamId, out var stream))
            {
                // Previous segment closed on interrupt — reopen with same stream id.
                OpenStream(streamId, agent, isThinking);
                stream = _streams[streamId];
            }

            var style = stream.IsThinking
                ? "grey italic"
                : stream.IsForUser || stream.To == "User"
                    ? "bold white"
                    : "yellow";

            WriteStyledChunk(style, text);
            stream.HasOpenStyle = true;
        }

        /// <summary>
        /// Spectre Markup often swallows '\n' inside a style span — emit line breaks explicitly.
        /// </summary>
        private static void WriteStyledChunk(string style, string text)
        {
            var parts = text.ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    AnsiConsole.Markup($"[{style}]{Markup.Escape(parts[i])}[/]");
                }

                if (i < parts.Length - 1)
                {
                    AnsiConsole.WriteLine();
                }
            }
        }

        private void OpenStream(
            Guid streamId,
            string? agent,
            bool isThinking,
            string? to = null,
            bool isForUser = false)
        {
            var startedAt = DateTime.Now;
            _streams[streamId] = new ActiveStream(agent, startedAt, isThinking, to, isForUser);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(FormatHeader(agent, to, isThinking, isForUser, startedAt)).LeftJustified());
        }

        private void CompleteStream(Guid streamId, long? inputTokens)
        {
            // Unknown stream id: do not emit an orphan usage-only footer (causes double rules).
            CloseStreamSegment(streamId, inputTokens);
        }

        private void PauseThinking(string? agent)
        {
            foreach (var streamId in _streams.Keys.ToList())
            {
                if (!_streams.TryGetValue(streamId, out var stream))
                {
                    continue;
                }

                if (!stream.IsThinking)
                {
                    continue;
                }

                if (agent is not null && stream.Agent != agent)
                {
                    continue;
                }

                CloseStreamSegment(streamId);
            }
        }

        private bool CloseStreamSegment(Guid streamId, long? inputTokens = null)
        {
            if (!_streams.Remove(streamId, out var stream))
            {
                return false;
            }

            CloseStyle(stream);
            var endedAt = DateTime.Now;
            AnsiConsole.Write(new Rule(FormatFooter(endedAt, endedAt - stream.StartedAt, inputTokens)).RightJustified());
            return true;
        }

        private static void CloseStyle(ActiveStream stream)
        {
            if (!stream.HasOpenStyle)
            {
                return;
            }

            AnsiConsole.WriteLine();
            stream.HasOpenStyle = false;
        }

        private static string FormatHeader(
            string? from,
            string? to,
            bool isThinking,
            bool isForUser,
            DateTime time)
        {
            if (isThinking)
            {
                return $"[grey]{from ?? "Unknown"} · thinking[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            if (isForUser || to == "User")
            {
                return $"[bold green]{from ?? "Unknown"} -> You[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            var toPart = string.IsNullOrEmpty(to) ? string.Empty : $" -> {to}";
            return $"[cyan]{from ?? "Unknown"}{toPart}[/] [grey][[{time:HH:mm:ss}]][/]";
        }

        private static string FormatFooter(DateTime endTime, TimeSpan duration, long? inputTokens)
        {
            var timing = $"took {duration.TotalSeconds:F1}s";
            var usage = UsageFormatter.Format(inputTokens);
            return string.IsNullOrEmpty(usage)
                ? $"[grey][[{endTime:HH:mm:ss}]] ({timing})[/]"
                : $"[grey][[{endTime:HH:mm:ss}]] ({timing}. {usage})[/]";
        }

        private static void WriteToolCall(Observation.ToolCallLogged msg)
        {
            var time = DateTime.Now;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[grey]{msg.Agent} · {msg.ToolName}[/] [grey][[{time:HH:mm:ss}]][/]").LeftJustified());

            if (!string.IsNullOrEmpty(msg.Payload))
            {
                foreach (var line in msg.Payload.ReplaceLineEndings("\n").Split('\n'))
                {
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(line)}[/]");
                }
            }

            AnsiConsole.Write(new Rule($"[grey][[{DateTime.Now:HH:mm:ss}]][/]").RightJustified());
        }

        private static void WriteNudge(Observation.NudgeLogged msg)
        {
            var time = DateTime.Now;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[yellow]{msg.Agent} · nudge[/] [grey][[{time:HH:mm:ss}]][/]").LeftJustified());

            foreach (var line in msg.Message.ReplaceLineEndings("\n").Split('\n'))
            {
                AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(line)}[/]");
            }

            AnsiConsole.Write(new Rule($"[grey][[{DateTime.Now:HH:mm:ss}]][/]").RightJustified());
        }

        private static void WriteError(Observation.ErrorLogged msg)
        {
            var time = DateTime.Now;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(
                $"[red]{msg.Agent} · error[/] [grey][[{time:HH:mm:ss}]][/]").LeftJustified());
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(msg.Message)}[/]");
            AnsiConsole.Write(new Rule($"[grey][[{DateTime.Now:HH:mm:ss}]][/]").RightJustified());
        }
    }
}
