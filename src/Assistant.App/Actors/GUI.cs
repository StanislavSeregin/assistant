using Proto;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class GUI
{
    public record RequestAsk;

    public record Streaming(string? Name, IAsyncEnumerable<string> LiveContent);

    private const string NAME = $"{nameof(GUI)}-{nameof(Actor)}";

    public static PID Spawn(ActorSystem system, Props props)
        => system.Root.SpawnNamed(props, NAME);

    public static PID? FindPid(ActorSystem system)
        => system.ProcessRegistry.Find(NAME).FirstOrDefault();

    public class Actor : IActor
    {
        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(),
                RequestAsk => Ask(context),
                Streaming streaming => RenderStreaming(context, streaming),
                _ => Task.CompletedTask
            };
        }

        private static Task Init()
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            AnsiConsole.Write(new FigletText("Assistant")
            {
                Justification = Justify.Center,
                Color = ConsoleColor.Cyan
            });

            return Task.CompletedTask;
        }

        private static Task Ask(IContext context)
        {
            AnsiConsole.WriteLine();
            var request = AnsiConsole.Ask<string>(">");
            if (context.Sender is { } pid)
            {
                var envelope = new MessageEnvelope(new Messages.Ask(request), context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private static async Task RenderStreaming(IContext context, Streaming response)
        {
            var startTime = DateTime.Now;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[cyan]{response.Name ?? "Unknown"}[/] [grey][[{startTime:HH:mm:ss}]][/]").LeftJustified());
            await foreach (var text in response.LiveContent)
            {
                AnsiConsole.Markup($"[yellow]{Markup.Escape(text)}[/]");
            }

            var endTime = DateTime.Now;
            var duration = endTime - startTime;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule($"[grey][[{endTime:HH:mm:ss}]] (took {duration.TotalSeconds:F1}s)[/]").RightJustified());
            if (context.Sender is { } pid)
            {
                var envelope = new MessageEnvelope(new Messages.Rendered(), context.Self);
                context.Send(pid, envelope);
            }
        }
    }
}
