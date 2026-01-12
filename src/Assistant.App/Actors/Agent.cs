using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using Proto.DependencyInjection;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Init(string Name, string Instructions);

    public record Ask(ChatRole Role, string? From, string? Content, string? To = null);

    public record NameRequest;

    public record NameResponse(string Name);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private record ToolInvocation(Func<IContext, Task<ToolResult?>> Func);

        private record ToolResult(string Name, string Content);

        private PID? Self { get; set; }

        private ActorSystem? System { get; set; }

        private ChatClientAgent? Agent { get; set; }

        private AgentThread? Thread { get; set; }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Init msg => Init(context, msg),
                Ask msg when msg.To is null || msg.To == Agent?.Name => HandleAsk(context, msg),
                NameRequest => RespondCurrentName(context),
                ToolInvocation msg => InvokeTool(context, msg),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context, Init msg)
        {
            Self = context.Self;
            System = context.System;
            Agent = CreateChatClientAgent(msg);
            Thread = Agent.GetNewThread();
        }

        private ChatClientAgent CreateChatClientAgent(Init msg)
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
                tools: [
                    AIFunctionFactory.Create(MessageSubordinateTool),
                    AIFunctionFactory.Create(RequestListSubordinatesTool),
                    AIFunctionFactory.Create(RequestHireTool)]);
        }

        private Task RespondCurrentName(IContext context)
        {
            if (context.Sender is { } pid)
            {
                var payload = new NameResponse(Agent?.Name ?? "Anonymous");
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private Task HandleAsk(IContext context, Ask msg)
        {
            if (Agent is not null)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(msg.Role, msg.Content)
                {
                    AuthorName = msg.From
                };

                var updates = Agent.RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken);
                var streaming = new User.Streaming(Agent.Name, To: default, WrapResponse(context, updates));
                context.System.EventStream.Publish(streaming);
            }

            return Task.CompletedTask;
        }

        private async IAsyncEnumerable<string> WrapResponse(IContext context, IAsyncEnumerable<AgentRunResponseUpdate> updates)
        {
            var sb = new StringBuilder();
            await foreach (var update in updates)
            {
                if (!string.IsNullOrWhiteSpace(update.Text))
                {
                    sb.Append(update.Text);
                    yield return update.Text;
                }
            }

            if (context.Sender is { } sender && sender != context.Self)
            {
                var payload = new Ask(ChatRole.Assistant, Agent?.Name, sb.ToString());
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(sender, envelope);
            }
        }

        private async Task InvokeTool(IContext context, ToolInvocation msg)
        {
            if (await msg.Func(context) is { } toolResult)
            {
                var streaming = User.Streaming.FromString(toolResult.Name, Agent?.Name, toolResult.Content);
                context.System.EventStream.Publish(streaming);

                var payload = new Ask(ChatRole.Tool, toolResult.Name, toolResult.Content);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(context.Self, envelope);
            }
        }

        [Description("Sends a message to the employee")]
        public string MessageSubordinateTool(
            [Description("Job title of the direct report (must match exactly from team roster)")] string subordinatePosition,
            [Description("Content to deliver (instruction, query, or feedback)")] string message)
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();
            var agent = Agent ?? throw new InvalidOperationException();

            system.EventStream.Publish(User.Streaming.FromString(Agent?.Name, subordinatePosition, message));

            var payload = new ToolInvocation(context =>
            {
                var payload = new Ask(ChatRole.Assistant, agent.Name, message, subordinatePosition);
                var envelope = new MessageEnvelope(payload, context.Self);
                foreach (var pid in context.Children)
                {
                    context.Send(pid, envelope);
                }

                return Task.FromResult(default(ToolResult));
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
            return "Message sent, wait for a reply.";
        }

        

        [Description("Returns the job titles of your current direct reports")]
        public string RequestListSubordinatesTool()
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();

            system.EventStream.Publish(User.Streaming.FromString(Agent?.Name, nameof(RequestListSubordinatesTool), "Called"));

            var payload = new ToolInvocation(async context =>
            {
                var names = await Task.WhenAll(context.Children.Select(async pid =>
                {
                    var payload = new NameRequest();
                    var envelope = new MessageEnvelope(payload, context.Self);
                    var response = await context.RequestAsync<NameResponse>(pid, envelope);
                    return response.Name;
                }));

                return new ToolResult(
                    Name: nameof(RequestListSubordinatesTool),
                    Content: $"Tool response: [{string.Join("; ", names)}]");
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
            return "Request sent, wait for a reply.";
        }

        [Description("Submits a hiring request for a new direct report to your manager")]
        public string RequestHireTool(
            [Description("Job title of the open position (e.g., 'Senior Analyst')")] string position,
            [Description("Official responsibilities and requirements for the role")] string jobDescription)
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();

            system.EventStream.Publish(User.Streaming.FromString(Agent?.Name, nameof(RequestHireTool), $"""
            {nameof(position)}: {position}
            {nameof(jobDescription)}: {jobDescription}
            """));

            var content = $"Employee '{position}' is now available!";
            var payload = new ToolInvocation(context =>
            {
                var props = context.System.DI().PropsFor<Actor>();
                var pid = context.Spawn(props);
                var payload = new Init(position, jobDescription);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
                var toolResult = new ToolResult(Name: nameof(RequestHireTool), Content: $"Employee '{position}' is now available!");
                return Task.FromResult(toolResult)!;
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
            return $"Request sent, wait for a reply.";
        }
    }
}
