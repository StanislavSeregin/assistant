using Assistant.App.Completions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using Proto;
using Proto.DependencyInjection;
using System;
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

    public class Actor(ChatClientFactory chatClientFactory) : IActor
    {
        private readonly Dictionary<string, PID> _employees = [];

        private ChatClientAgent? _agent;

        private AgentThread? _thread;

        private IContext? _context;

        private ChatClientAgent Agent => _agent ?? throw new InvalidOperationException();

        private AgentThread Thread => _thread ?? throw new InvalidOperationException();

        private IContext Context => _context ?? throw new InvalidOperationException();

        public async Task ReceiveAsync(IContext context)
        {
            try
            {
                _context = context;
                await (context.Message switch
                {
                    Init msg => Init(msg),
                    Email msg when msg.To is null || msg.To == Agent.Name => HandleEmail(msg),
                    NameRequest => RespondCurrentName(),
                    _ => Task.CompletedTask
                });
            }
            finally
            {
                _context = null;
            }
        }

        private async Task Init(Init msg)
        {
            _agent = CreateChatClientAgent(msg);
            _thread = _agent.GetNewThread();
        }

        private ChatClientAgent CreateChatClientAgent(Init msg)
        {
            return chatClientFactory.GetChatClient().CreateAIAgent(
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

        private async Task HandleEmail(Email msg)
        {
            var chatMessage = new Microsoft.Extensions.AI.ChatMessage()
            {
                Role = ChatRole.Assistant,
                AuthorName = msg.From,
                Contents = [new TextContent($"""
                    NEW EMAIL - ACTION REQUIRED!
                    From: {msg.From}
                    Subject: {msg.Subject}
                    Body:
                    {msg.Body}

                    IMPORTANT: You must respond using your tools only:
                    - Use MessageSupervisorTool to reply to your manager
                    - Use MessageSubordinateTool to delegate tasks
                    - DO NOT send plain text replies - communicate via email tools only
                    """)]
            };

            var response = await Agent.RunAsync(chatMessage, Thread, cancellationToken: Context.CancellationToken);
            var log = new User.MessageLog(Agent.Name, To: default, $"[Think] {response.Text}");
            Context.System.EventStream.Publish(log);
        }

        private Task RespondCurrentName()
        {
            if (Context.Sender is { } pid && Agent?.Name is { } name)
            {
                var payload = new NameResponse(name);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        [Description("Send email to supervisor")]
        public void MessageSupervisorTool(
            [Description("Subject")] string subject,
            [Description("Body")] string body)
        {
            if (Context.Parent is { } pid)
            {
                var log = new User.MessageLog(Agent.Name, "Supervisor", $"[{subject}] {body}");
                Context.System.EventStream.Publish(log);
                var payload = new Email(Agent.Name, To: default, subject, body);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);
            }
        }

        [Description("Send email to employee")]
        public void MessageSubordinateTool(
            [Description("Recipient")] string to,
            [Description("Subject")] string subject,
            [Description("Body")] string body)
        {
            var log = new User.MessageLog(Agent.Name, to, $"[{subject}] {body}");
            Context.System.EventStream.Publish(log);
            var payload = new Email(Agent.Name, to, subject, body);
            var envelope = new MessageEnvelope(payload, Context.Self);
            foreach (var pid in Context.Children)
            {
                Context.Send(pid, envelope);
            }
        }

        [Description("Get own employees")]
        public string RequestListSubordinatesTool()
        {
            var response = _employees.Keys.Count > 0
                ? $"[{string.Join("; ", _employees.Keys)}]"
                : "No employees";

            var log = new User.MessageLog(nameof(RequestListSubordinatesTool), Agent.Name, response);
            Context.System.EventStream.Publish(log);
            return response;
        }

        [Description("Hire a new employee")]
        public async Task<string> RequestHireTool(
            [Description("Job title of the open position (e.g., 'Senior Analyst')")] string position,
            [Description("Official responsibilities and requirements for the role")] string jobDescription)
        {
            Context.System.EventStream.Publish(new User.MessageLog(Agent.Name, nameof(RequestHireTool), $"""
            {nameof(position)}: {position}
            {nameof(jobDescription)}: {jobDescription}
            """));

            if (_employees.ContainsKey(position) is false)
            {
                var resume = await HR.GetResume(chatClientFactory.GetChatClient(), position, jobDescription, Context.CancellationToken);
                var instructions = $"""
                Your position:
                {position}

                Your resume:
                {resume}
                """;

                var props = Context.System.DI().PropsFor<Actor>();
                var pid = Context.Spawn(props);
                _employees.Add(position, pid);
                var payload = new Init(position, instructions);
                var envelope = new MessageEnvelope(payload, Context.Self);
                Context.Send(pid, envelope);
            }

            return $"Employee '{position}' successfully hired!";
        }
    }
}
