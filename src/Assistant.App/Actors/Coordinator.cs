using Assistant.App.Functions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using System;
using System.ClientModel;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Coordinator
{
    public record RunSubagent(string Name, string Instructions, string Message);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private ChatClientAgent? Agent { get; set; }

        private AgentThread? Thread { get; set; }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(context),
                Ask msg => HandleAsk(context, msg),
                Rendered => RequestAsk(context),
                RunSubagent msg => RunSubagent(context, msg),
                _ => Task.CompletedTask
            };
        }

        private Task Init(IContext context)
        {
            var apiKeyCredential = new ApiKeyCredential(options.Value.ApiKey);
            var openAIClientOptions = new OpenAIClientOptions()
            {
                Endpoint = new Uri(options.Value.Endpoint)
            };

            var openAIClient = new OpenAIClient(apiKeyCredential, openAIClientOptions);
            var subagentFunctions = new SubagentFunctions(context);
            Agent = openAIClient
                .GetChatClient(options.Value.ModelName)
                .CreateAIAgent(
                    name: "Coordinator",
                    instructions: """
                    Ты полезный ассистент.
                    """,
                    tools: [AIFunctionFactory.Create(subagentFunctions.RunSubagent)]);

            Thread = Agent.GetNewThread();
            return RequestAsk(context);
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

        private Task HandleAsk(IContext context, Ask msg)
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
