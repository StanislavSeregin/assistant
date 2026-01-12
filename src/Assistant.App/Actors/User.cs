using Proto;
using Proto.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public record MessageLog(string? From, string? To, string? Content);

    public class Actor : IActor
    {
        private const string AGENT_NAME = "Secretary";

        private const string AGENT_INSTRUCTIONS = """
        You're a wise secretary and an effective manager.
        Delegate complex tasks to your staff and supervise them.
        """;

        private readonly SemaphoreSlim _streamingLock = new(1);

        private readonly List<EventStreamSubscription<object>> _subscriptions = [];

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(context),
                Agent.Email when context.Sender is { } pid => HandleAsk(context, pid),
                Stopped => Unsubscribe(),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Clear();
            AnsiConsole.Write(new FigletText("Assistant")
            {
                Justification = Justify.Center,
                Color = ConsoleColor.Cyan
            });

            RegisterSubscriptions(context);
            var pid = SpawnAgent(context);
            await HandleAsk(context, pid);
        }

        private void RegisterSubscriptions(IContext context)
        {
            _subscriptions.Add(context.System.EventStream.Subscribe<MessageLog>(RenderLog));
        }

        private Task Unsubscribe()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Unsubscribe();
            }

            return Task.CompletedTask;
        }

        private static PID SpawnAgent(IContext context)
        {
            var props = context.System.DI().PropsFor<Agent.Actor>();
            var pid = context.Spawn(props);
            var payload = new Agent.Init(AGENT_NAME, AGENT_INSTRUCTIONS);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
            return pid;
        }

        private async Task HandleAsk(IContext context, PID pid)
        {
            await _streamingLock.WaitAsync();
            try
            {
                AnsiConsole.WriteLine();
                var input = AnsiConsole.Prompt(new TextPrompt<string>(">").AllowEmpty());
                var payload = new Agent.Email(From: "User", To: default, "Request", Body: input);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }
            finally
            {
                _streamingLock.Release();
            }
        }

        private async Task RenderLog(MessageLog msg)
        {
            await _streamingLock.WaitAsync();
            try
            {
                var startTime = DateTime.Now;
                var from = msg.From ?? "Unknown";
                var to = !string.IsNullOrEmpty(msg.To)
                    ? $" -> {msg.To}"
                    : string.Empty;

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[cyan]{from}{to}[/] [grey][[{startTime:HH:mm:ss}]][/]").LeftJustified());
                AnsiConsole.Markup($"[yellow]{Markup.Escape(msg.Content ?? "[EMPTY]")}[/]");
                var endTime = DateTime.Now;
                var duration = endTime - startTime;
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[grey][[{endTime:HH:mm:ss}]] (took {duration.TotalSeconds:F1}s)[/]").RightJustified());
            }
            finally
            {
                _streamingLock.Release();
            }
        }
    }
}
