using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class SubAgent
{
    public record RunSubAgent(string ParentName, string Name, string Instructions, string Message);

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
                RunSubAgent msg => Init(context, msg),
                Messages.Rendered => SendResponseToParent(context),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context, RunSubAgent msg)
        {
            Self = context.Self;
            System = context.System;
            Agent = CreateChatClientAgent(msg);
            Thread = Agent.GetNewThread();
            await Run(context, msg.Message, msg.ParentName);
        }

        private ChatClientAgent CreateChatClientAgent(RunSubAgent msg)
        {
            var apiKeyCredential = new ApiKeyCredential(options.Value.ApiKey);
            var openAIClientOptions = new OpenAIClientOptions()
            {
                Endpoint = new Uri(options.Value.Endpoint)
            };

            var openAIClient = new OpenAIClient(apiKeyCredential, openAIClientOptions);
            return openAIClient.GetChatClient(options.Value.ModelName).CreateAIAgent(
                name: msg.Name,
                instructions: msg.Instructions,
                tools: [/* TODO */]);
        }

        private async Task Run(IContext context, string message, string parentName)
        {
            if (Agent is not null)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, message)
                {
                    AuthorName = parentName
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
        }

        private async Task SendResponseToParent(IContext context)
        {
            if (context.Parent is { } parent
                && Thread?.GetService<IList<Microsoft.Extensions.AI.ChatMessage>>() is { } messages
                && messages.LastOrDefault(m => m.Role == ChatRole.Assistant && m.AuthorName == Agent?.Name) is { } lastAnswer)
            {
                var payload = new Messages.ResponseFromSubAgent(lastAnswer.AuthorName, lastAnswer.Text);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(parent, envelope);
            }
        }
    }
}
