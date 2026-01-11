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

public static class Agent
{
    public record Init(string Name, string Instructions);

    public record Ask(ChatRole Role, string? From, string? Content, string? To = null);

    public record NameRequest;

    public record NameResponse(string Name);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private record ToolInvocation(Func<IContext , Task> Func);

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
                User.Streaming => ForwardToParent(context),
                ToolInvocation msg => msg.Func(context),
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
            return openAIClient
                .GetChatClient(options.Value.ModelName)
                .CreateAIAgent(name: msg.Name, instructions: msg.Instructions, tools: [
                    AIFunctionFactory.Create(MessageSupervisorTool),
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
            if (Agent is not null && context.Sender is { } pid)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(msg.Role, msg.Content)
                {
                    AuthorName = msg.From
                };

                var updates = Agent.RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken);
                var stream = updates.Where(update => !string.IsNullOrEmpty(update.Text)).Select(update => update.Text);
                var payload = new User.Streaming(Agent.Name, stream);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private static Task ForwardToParent(IContext context)
        {
            if (context.Parent is { } pid)
            {
                context.Forward(pid);
            }

            return Task.CompletedTask;
        }

        [Description("Sends a message to your direct supervisor")]
        public void MessageSupervisorTool([Description("Content for your immediate manager (report, request, or notification)")] string message)
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();
            var agent = Agent ?? throw new InvalidOperationException();
            var payload = new ToolInvocation(async context =>
            {
                if (context.Parent is { } pid)
                {
                    var payload = new Ask(ChatRole.Assistant, agent.Name, message);
                    var envelope = new MessageEnvelope(payload, context.Self);
                    context.Send(pid, envelope);
                }
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
        }

        [Description("Sends a message to the employee holding the specified position")]
        public void MessageSubordinateTool(
            [Description("Job title of the direct report (must match exactly from team roster)")] string subordinatePosition,
            [Description("Content to deliver (instruction, query, or feedback)")] string message)
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();
            var agent = Agent ?? throw new InvalidOperationException();
            var payload = new ToolInvocation(context =>
            {
                var payload = new Ask(ChatRole.Assistant, agent.Name, message, subordinatePosition);
                var envelope = new MessageEnvelope(payload, context.Self);
                foreach (var pid in context.Children)
                {
                    context.Send(pid, envelope);
                }

                return Task.CompletedTask;
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
        }

        

        [Description("Returns the job titles of your current direct reports")]
        public void RequestListSubordinatesTool()
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();
            var payload = new ToolInvocation(async context =>
            {
                var names = await Task.WhenAll(context.Children.Select(pid =>
                {
                    var payload = new NameRequest();
                    var envelope = new MessageEnvelope(payload, context.Self);
                    return context.RequestAsync<NameResponse>(pid, envelope);
                }));

                var message = $"Tool response: [{string.Join("; ", names)}]";
                var payload = new Ask(ChatRole.Tool, nameof(RequestListSubordinatesTool), message);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(context.Self, envelope);
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
        }

        [Description("Submits a hiring request for a new direct report to your manager")]
        public void RequestHireTool(
            [Description("Job title of the open position (e.g., 'Senior Analyst')")] string position,
            [Description("Official responsibilities and requirements for the role")] string jobDescription)
        {
            var self = Self ?? throw new InvalidOperationException();
            var system = System ?? throw new InvalidOperationException();
            var payload = new ToolInvocation(context =>
            {
                var props = context.System.DI().PropsFor<Actor>();
                var pid = context.Spawn(props);
                var payload = new Init(position, jobDescription);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
                return Task.CompletedTask;
            });

            var envelope = new MessageEnvelope(payload, self);
            system.Root.Send(self, envelope);
        }
    }
}
