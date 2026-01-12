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
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Init(string Name, string Instructions);

    public record Email(string? From, string? To, string Subject, string Body);

    public record NameRequest;

    public record NameResponse(string Name);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private record InternalState(PID Self, ActorSystem System, ChatClientAgent Agent, AgentThread Thread);

        private record InternalToolInvocation(Func<IContext, Task> Func);

        private InternalState? _state;

        private readonly Dictionary<string, PID> _employees = [];

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Init msg => Init(context, msg),
                Email msg when msg.To is null || msg.To == _state?.Agent.Name => HandleEmail(context, msg),
                NameRequest => RespondCurrentName(context),
                InternalToolInvocation msg => msg.Func(context),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context, Init msg)
        {
            var agent = CreateChatClientAgent(msg);
            var thread = agent.GetNewThread();
            _state = new InternalState(context.Self, context.System, agent, thread);
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
                instructions: $"""
                == Communication Rules ==
                - All communication is conducted via email
                - Always answer in the same language
                - If you have any questions about a task, ask your manager
                - Don't hesitate to correct your manager and offer better solutions
                - You are responsible for your subordinates
                - Set clear and understandable tasks for your subordinates
                - Achieve high-quality results from your subordinates
                - Report your results to your manager

                == Personal Instructions ==
                {msg.Instructions}
                """,
                tools: [
                    AIFunctionFactory.Create(MessageSupervisorTool),
                    AIFunctionFactory.Create(MessageSubordinateTool),
                    AIFunctionFactory.Create(RequestListSubordinatesTool),
                    AIFunctionFactory.Create(RequestHireTool)]);
        }

        private async Task HandleEmail(IContext context, Email msg)
        {
            if (_state is { } state)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage()
                {
                    Role = ChatRole.Tool,
                    AuthorName = "Mailing",
                    Contents = [new TextContent($"""
                    New email received!
                    From: {msg.From}
                    Subject: {msg.Subject}
                    Body:
                    {msg.Body}
                    """)]
                };

                var response = await state.Agent.RunAsync(chatMessage);
                var log = new User.MessageLog(state.Agent.Name, To: default, $"[Think] {response.Text}");
                context.System.EventStream.Publish(log);
            }
        }

        private Task RespondCurrentName(IContext context)
        {
            if (context.Sender is { } pid && _state?.Agent?.Name is { } name)
            {
                var payload = new NameResponse(name);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        [Description("Send email to supervisor")]
        public void MessageSupervisorTool(
            [Description("Subject")] string subject,
            [Description("Body")] string body)
        {
            var state = _state ?? throw new InvalidOperationException();
            var payload = new InternalToolInvocation(context =>
            {
                if (context.Parent is { } pid)
                {
                    var log = new User.MessageLog(state.Agent.Name, "Supervisor", $"[{subject}] {body}");
                    context.System.EventStream.Publish(log);

                    var payload = new Email(state.Agent.Name, To: default, subject, body);
                    var envelope = new MessageEnvelope(payload, context.Self);
                    context.Send(pid, envelope);
                }

                return Task.CompletedTask;
            });

            var envelope = new MessageEnvelope(payload, state.Self);
            state.System.Root.Send(state.Self, envelope);
        }

        [Description("Send email to employee")]
        public void MessageSubordinateTool(
            [Description("Recipient")] string to,
            [Description("Subject")] string subject,
            [Description("Body")] string body)
        {
            var state = _state ?? throw new InvalidOperationException();
            var payload = new InternalToolInvocation(context =>
            {
                var log = new User.MessageLog(state.Agent.Name, to, $"[{subject}] {body}");
                context.System.EventStream.Publish(log);

                var payload = new Email(state.Agent.Name, to, subject, body);
                var envelope = new MessageEnvelope(payload, context.Self);
                foreach (var pid in context.Children)
                {
                    context.Send(pid, envelope);
                }

                return Task.CompletedTask;
            });

            var envelope = new MessageEnvelope(payload, state.Self);
            state.System.Root.Send(state.Self, envelope);
        }

        [Description("Get own employees")]
        public string[] RequestListSubordinatesTool()
        {
            if (_state is { } state)
            {
                var log = new User.MessageLog(nameof(RequestListSubordinatesTool), state.Agent.Name, $"[{string.Join("; ", _employees.Keys)}]");
                state.System.EventStream.Publish(log);
            }

            return [.. _employees.Keys];
        }

        [Description("Hire a new employee")]
        public string RequestHireTool(
            [Description("Job title of the open position (e.g., 'Senior Analyst')")] string position,
            [Description("Official responsibilities and requirements for the role")] string jobDescription)
        {
            var state = _state ?? throw new InvalidOperationException();
            var payload = new InternalToolInvocation(context =>
            {
                state.System.EventStream.Publish(new User.MessageLog(state.Agent.Name, nameof(RequestHireTool), $"""
                {nameof(position)}: {position}
                {nameof(jobDescription)}: {jobDescription}
                """));

                if (_employees.ContainsKey(position) is false)
                {
                    var props = context.System.DI().PropsFor<Actor>();
                    var pid = context.Spawn(props);
                    _employees.Add(position, pid);
                    var payload = new Init(position, jobDescription);
                    var envelope = new MessageEnvelope(payload, context.Self);
                    context.Send(pid, envelope);
                }

                return Task.CompletedTask;
            });

            var envelope = new MessageEnvelope(payload, state.Self);
            state.System.Root.Send(state.Self, envelope);
            return $"Employee '{position}' successfully hired!";
        }
    }
}
