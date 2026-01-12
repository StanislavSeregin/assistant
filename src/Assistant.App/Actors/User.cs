using Microsoft.Extensions.AI;
using Proto;
using Proto.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public record Streaming(string? From, string? To, IAsyncEnumerable<string> LiveContent)
    {
        public static Streaming FromString(string? from, string? to, string? content)
        {
            var stream = AsyncEnumerable.Empty<string>().Prepend(content ?? string.Empty);
            return new Streaming(from, to, stream);
        }
    }

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
                // Agent.Ask when context.Sender is { } pid => HandleAsk(context, pid),
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
            _subscriptions.Add(context.System.EventStream.Subscribe<Streaming>(RenderStreaming));
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

        private static async Task HandleAsk(IContext context, PID pid)
        {
            await Task.Delay(100);
            AnsiConsole.WriteLine();
            var content = AnsiConsole.Ask<string>(">");
            var payload = new Agent.Ask(ChatRole.User, From: "User", Content: content);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
        }

        private async Task RenderStreaming(Streaming msg)
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
                await foreach (var text in msg.LiveContent)
                {
                    AnsiConsole.Markup($"[yellow]{Markup.Escape(text)}[/]");
                }

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
