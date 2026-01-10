using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using Proto.DependencyInjection;
using System;
using System.ClientModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class MainAgent
{
    public record RunSubAgent(string Name, string Instructions, string Message);

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
                Messages.Rendered => Task.CompletedTask, // RequestAsk(context) // TODO: Add condition
                RunSubAgent msg => RunSubAgent(context, msg),
                Messages.ResponseFromSubAgent msg => HandleResponseFromSubAgent(context, msg),
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
                var payload = new RunSubAgent(name, instructions, message);
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
            if (Agent is not null)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, msg.Text);
                var liveContent = Agent
                    .RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken)
                    .Where(update => !string.IsNullOrEmpty(update.Text))
                    .Select(update => update.Text);

                var streaming = new GUI.Streaming(Agent.Name, liveContent);
                if (GUI.FindPid(context.System) is { } pid)
                {
                    var envelope = new MessageEnvelope(streaming, context.Self);
                    context.Send(pid, envelope);
                }
            }

            return Task.CompletedTask;
        }

        private Task RunSubAgent(IContext context, RunSubAgent msg)
        {
            var parentName = Agent?.Name ?? "Assistant";
            var payload = new SubAgent.RunSubAgent(parentName, msg.Name, msg.Instructions, msg.Message);
            var envelope = new MessageEnvelope(payload, context.Self);
            var props = context.System.DI().PropsFor<SubAgent.Actor>();
            var pid = context.Spawn(props);
            context.Send(pid, envelope);
            return Task.CompletedTask;
        }

        private Task HandleResponseFromSubAgent(IContext context, Messages.ResponseFromSubAgent msg)
        {
            if (Agent is not null)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, msg.Text)
                {
                    AuthorName = msg.Name
                };

                var liveContent = Agent
                    .RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken)
                    .Where(update => !string.IsNullOrEmpty(update.Text))
                    .Select(update => update.Text);

                var streaming = new GUI.Streaming(Agent.Name, liveContent);
                if (GUI.FindPid(context.System) is { } pid)
                {
                    var envelope = new MessageEnvelope(streaming, context.Self);
                    context.Send(pid, envelope);
                }
            }

            return Task.CompletedTask;
        }
    }
}
