using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using System;
using System.ClientModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class MainAgent
{
    public record RunSubagent(string Name, string Instructions, string Message);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private PID? Self { get; set; }

        private ActorSystem? System { get; set; }

        private ChatClientAgent? Agent { get; set; }

        private AgentThread? Thread { get; set; }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(context),
                Messages.Ask msg => HandleAsk(context, msg),
                Messages.Rendered => RequestAsk(context),
                RunSubagent msg => RunSubagent(context, msg),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context)
        {
            await RequestAsk(context);
            Self = context.Self;
            System = context.System;
            Agent = CreateChatClientAgent();
            Thread = Agent.GetNewThread();
        }

        private ChatClientAgent CreateChatClientAgent()
        {
            var apiKeyCredential = new ApiKeyCredential(options.Value.ApiKey);
            var openAIClientOptions = new OpenAIClientOptions()
            {
                Endpoint = new Uri(options.Value.Endpoint)
            };

            var openAIClient = new OpenAIClient(apiKeyCredential, openAIClientOptions);
            return openAIClient.GetChatClient(options.Value.ModelName).CreateAIAgent(
                name: "Assistant",
                instructions: """
                You are a helpful assistant.
                """,
                tools: [AIFunctionFactory.Create(RunSubagentTool)]);
        }

        [Description("Launch a specialized subagent to handle specific tasks")]
        public void RunSubagentTool(
        [Description("Unique name identifying the subagent")] string name,
        [Description("System instructions defining the subagent's behavior and capabilities")] string instructions,
        [Description("Initial message or task to send to the subagent")] string message)
        {
            if (System is not null && Self is not null)
            {
                var payload = new RunSubagent(name, instructions, message);
                var envelope = new MessageEnvelope(payload, Self);
                System.Root.Send(Self, envelope);
            }
        }

        private static Task RequestAsk(IContext context)
        {
            if (GUI.FindPid(context.System) is { } pid)
            {
                var envelope = new MessageEnvelope(new GUI.RequestAsk(), context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private Task HandleAsk(IContext context, Messages.Ask msg)
        {
            var chatMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, msg.Text);
            if (Agent is { } agent)
            {
                var liveContent = agent
                    .RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken)
                    .Where(update => !string.IsNullOrEmpty(update.Text))
                    .Select(update => update.Text);

                var message = new GUI.Streaming(agent.Name, liveContent);
                if (GUI.FindPid(context.System) is { } pid)
                {
                    var envelope = new MessageEnvelope(message, context.Self);
                    context.Send(pid, envelope);
                }
            }

            return Task.CompletedTask;
        }

        private async Task RunSubagent(IContext context, RunSubagent msg)
        {
            // TODO
        }
    }
}
