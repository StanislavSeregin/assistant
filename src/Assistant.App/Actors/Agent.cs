using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Proto;
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Metadata(string Name, string Description, string Instructions, bool IsMaster);

    public class Actor(ChatClientFactory chatClientFactory) : IActor
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
            == Personal Info ==
            
            Name: {Metadata.Name}
            Description:
            {Metadata.Description}
            
            == Personal Instructions ==
            
            {Metadata.Instructions}
            
            == Communication Rules ==
            
            1. INCOMING MESSAGES:
            - You receive messages from other participants
            - Messages arrive in batches - this is normal and efficient
            - Each message is independent, but analyze the batch for overall context
            
            2. HOW TO ANALYZE:
            - Look at the sender's name and message content
            - Determine what each participant is writing about
            - If one person sent multiple messages - combine their meaning
            
            3. HOW TO RESPOND:
            - Provide a separate response to each message
            - Address each response to the specific participant
            - Request additional details if needed
            - Confirm when an issue is resolved
            
            4. EFFICIENCY:
            - Write clearly and to the point
            - One message = one complete thought
            - Respect other participants' time

            5. YOU MUST UNDERSTAND THAT YOUR MESSAGES ARE NOT VISIBLE TO PARTICIPANTS, ALWAYS USE `{nameof(SendMessageTool)}`

            == Cautions ==
            - No meetings, only correspondence
            - No scheduling
            - Don't repeat yourself or quote each other without reason
            - Less emotion
            - Try to take into account the specific role of the other person in your correspondence
            - Only relevant information
            """;

            if (Metadata.IsMaster)
            {
                instructions = $"""
                {instructions}

                YOU ARE RESPONSIBLE FOR TASK COMPLETION AND COORDINATION

                YOUR CORE RESPONSIBILITIES:
                - Ensure assigned goals are achieved
                - Organize effective collaborative work
                - Monitor progress and quality
                - Report to the task assigner (visible in your participants list)

                YOUR UNIQUE CAPABILITIES:
                1. You can bring in new participants with needed specializations
                2. You can report progress and ask questions directly to the task assigner
                3. You receive final results from all participants

                HOW YOU WORK:
                1. Analyze what specializations are needed to complete tasks
                2. Bring in participants with required skills
                3. Assign specific tasks to specific people
                4. Collect results and report on progress
                5. Ask for clarifications if anything is unclear about the original task

                YOUR PRINCIPLES:
                - Clear task assignment
                - Regular oversight without micromanagement
                - Willingness to help and explain
                - Focus on results, not just process
                - Proactive communication about progress and challenges
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
            var content = string.Join(Environment.NewLine, msg.Messages.Select(m => $"""
            Got new message from '{m.From}':
            {m.Content}
            """));

            await RunAgent(new Microsoft.Extensions.AI.ChatMessage()
            {
                Role = ChatRole.Assistant,
                Contents = [new TextContent(content)]
            });

            Ready();
        }

        private async Task RunAgent(Microsoft.Extensions.AI.ChatMessage chatMessage)
        {
            var response = await Agent.RunAsync(chatMessage, Session, cancellationToken: Context.CancellationToken);
            var log = new User.MessageLog(Metadata.Name, To: "SELF", $"{response.Text}");
            Context.System.EventStream.Publish(log);
        }

        [Description("Send message")]
        private async Task<string> SendMessageTool(
            [Description("Recipient")] string to,
            [Description("Message")] string content)
        {
            var pid = Context.Parent ?? throw new InvalidOperationException();
            var participants = await GetParticipantsTool();
            if (participants.Any(p => p.Name == to))
            {
                var payload = new AgentRegistry.Message(Metadata.Name, to, content);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);

                var log = new User.MessageLog(Metadata.Name, to, content);
                Context.System.EventStream.Publish(log);

                return "Sent";
            }
            else
            {
                return $"'{to}' recipient not found. Use `{nameof(GetParticipantsTool)}` for getting participants.";
            }
        }

        [Description("Get participants for messaging")]
        private async Task<Participants[]> GetParticipantsTool()
        {
            if (Context.Parent is { } pid)
            {
                var request = new AgentRegistry.AgentsRequest();
                var agentsResponse = await Context.RequestAsync<AgentRegistry.AgentsResponse>(pid, request, Context.CancellationToken);
                var participants = agentsResponse.Agents
                    .Where(a => a.Name != Metadata.Name)
                    .Select(a => new Participants(a.Name, a.Description));

                if (Metadata.IsMaster)
                {
                    participants = [new Participants("User", "Director"), .. participants];
                }

                return [.. agentsResponse.Agents
                    .Where(a => a.Name != Metadata.Name)
                    .Select(a => new Participants(a.Name, a.Description))];
            }
            else
            {
                return [];
            }
        }

        [Description("Bring in a new participant for collaborative work")]
        private void CreateNewParticipantTool(
            [Description("Role of the new participant (e.g.: MobileDeveloper, Tester, Analyst)")] string name,
            [Description("What this participant will do, their responsibilities and specialization (very briefly)")] string description,
            [Description("Detailed instructions for the new participant: what to do, how to work, expected results")] string instructions)
        {
            if (Context.Parent is { } pid)
            {
                var payload = new Metadata(name, description, instructions, IsMaster: false);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);
            }
        }
    }
}
