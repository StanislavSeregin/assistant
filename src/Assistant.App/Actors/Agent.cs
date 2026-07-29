using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Proto;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Metadata(string Name, string Description, string Instructions, bool IsMaster);

    public class Actor(
        ChatClientFactory chatClientFactory,
        AgentConcurrencyLimiter concurrencyLimiter) : IActor
    {
        private record Participants(string Name, string Description);

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

            var instructions = $"""
                You are {Metadata.Name}. {Metadata.Description}

                {Metadata.Instructions}

                Rules:
                - Use {nameof(SendMessageTool)} for ALL messages to others, including User.
                - Free text is internal thinking only — the user never sees it.
                - After you finish tool work, always deliver the final answer to User with {nameof(SendMessageTool)}.
                - Use {nameof(GetParticipantsTool)} before messaging someone new.
                - Be brief and practical.
                """;

            if (Metadata.IsMaster)
            {
                instructions += $"""

                    You are the coordinator. User is the task assigner.
                    Use {nameof(CreateNewParticipantTool)} to add specialists when needed.
                    Never summarize results only in thinking — send them to User via {nameof(SendMessageTool)}.
                    """;
            }

            Agent = chatClientFactory.GetChatClient().AsAIAgent(
                name: Metadata.Name,
                description: Metadata.Description,
                instructions: instructions,
                tools: Metadata.IsMaster
                    ? [AIFunctionFactory.Create(SendMessageTool), AIFunctionFactory.Create(GetParticipantsTool), AIFunctionFactory.Create(CreateNewParticipantTool)]
                    : [AIFunctionFactory.Create(SendMessageTool), AIFunctionFactory.Create(GetParticipantsTool)]);

            Session = await Agent.CreateSessionAsync(Context.CancellationToken);
            Ready();
        }

        private void Ready()
        {
            var pid = Context.Parent ?? throw new InvalidOperationException();
            var payload = new AgentRegistry.Ready(Metadata.Name);
            var envelope = new MessageEnvelope(payload, Context.Self);
            Context.Send(pid, envelope);
        }

        private async Task HandleMessages(AgentRegistry.ReceivedMessages msg)
        {
            var content = string.Join(Environment.NewLine, msg.Messages.Select(m => $"[{m.From}]: {m.Content}"));

            await RunAgent(new Microsoft.Extensions.AI.ChatMessage()
            {
                Role = ChatRole.User,
                Contents = [new TextContent(content)]
            });

            Ready();
        }

        private Task RunAgent(Microsoft.Extensions.AI.ChatMessage chatMessage)
        {
            return concurrencyLimiter.RunAsync(async () =>
            {
                var streamId = Guid.NewGuid();
                Context.System.EventStream.Publish(new User.MessageLog(
                    Metadata.Name,
                    To: null,
                    Content: null,
                    StreamId: streamId,
                    IsStreamStart: true,
                    IsThinking: true));

                var updates = new List<AgentResponseUpdate>();
                await foreach (var update in Agent.RunStreamingAsync(
                    chatMessage,
                    Session,
                    cancellationToken: Context.CancellationToken))
                {
                    updates.Add(update);
                    if (string.IsNullOrEmpty(update.Text))
                    {
                        continue;
                    }

                    Context.System.EventStream.Publish(new User.MessageLog(
                        Metadata.Name,
                        To: null,
                        Content: update.Text,
                        StreamId: streamId,
                        IsThinking: true));
                }

                var usage = updates.ToAgentResponse().Usage;
                Context.System.EventStream.Publish(new User.MessageLog(
                    Metadata.Name,
                    To: null,
                    Content: null,
                    StreamId: streamId,
                    IsStreamComplete: true,
                    IsThinking: true,
                    InputTokens: usage?.InputTokenCount));
            }, Context.CancellationToken);
        }

        [Description("Send message")]
        private async Task<string> SendMessageTool(
            [Description("Recipient")] string to,
            [Description("Message")] string content)
        {
            var pid = Context.Parent ?? throw new InvalidOperationException();
            var participants = await GetParticipantsInternal();
            if (participants.Any(p => p.Name == to))
            {
                Context.System.EventStream.Publish(new User.MessageLog(
                    Metadata.Name,
                    To: to,
                    Content: content,
                    IsForUser: to == "User"));

                var payload = new AgentRegistry.Message(Metadata.Name, to, content);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);

                return "Sent";
            }

            return $"'{to}' recipient not found. Use `{nameof(GetParticipantsTool)}` for getting participants.";
        }

        [Description("Get participants for messaging")]
        private async Task<Participants[]> GetParticipantsTool() =>
            await GetParticipantsInternal();

        private async Task<Participants[]> GetParticipantsInternal()
        {
            if (Context.Parent is { } pid)
            {
                var request = new AgentRegistry.AgentsRequest();
                var agentsResponse = await Context.RequestAsync<AgentRegistry.AgentsResponse>(pid, request, Context.CancellationToken);
                var participants = agentsResponse.Agents
                    .Where(a => a.Name != Metadata.Name)
                    .Select(a => new Participants(a.Name, a.Description))
                    .ToList();

                if (Metadata.IsMaster)
                {
                    participants.Insert(0, new Participants("User", "Director"));
                }

                return [.. participants];
            }

            return [];
        }

        [Description("Bring in a new participant for collaborative work")]
        private void CreateNewParticipantTool(
            [Description("Role of the new participant (e.g.: MobileDeveloper, Tester, Analyst)")] string name,
            [Description("What this participant will do, their responsibilities and specialization (very briefly)")] string description,
            [Description("Detailed instructions for the new participant: what to do, how to work, expected results")] string instructions)
        {
            if (Context.Parent is { } pid)
            {
                User.PublishToolCall(Context, Metadata.Name, nameof(CreateNewParticipantTool),
                    ("name", name),
                    ("description", description),
                    ("instructions", instructions));

                var payload = new Metadata(name, description, instructions, IsMaster: false);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);
            }
        }
    }
}
