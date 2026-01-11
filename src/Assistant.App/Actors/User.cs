using Microsoft.Extensions.AI;
using Proto;
using Proto.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public record Streaming(string? Name, IAsyncEnumerable<string> LiveContent);

    public class Actor : IActor
    {
        private const string AGENT_NAME = "Secretary";

        private const string AGENT_INSTRUCTIONS = """
        You're a wise secretary and an effective manager.
        Delegate complex tasks to your staff and supervise them.
        """;

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(context),
                Agent.Ask when context.Sender is { } pid => HandleAsk(context, pid),
                Streaming msg => RenderStreaming(msg),
                _ => Task.CompletedTask
            };
        }

        private static async Task Init(IContext context)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            AnsiConsole.Write(new FigletText("Assistant")
            {
                Justification = Justify.Center,
                Color = ConsoleColor.Cyan
            });

            var pid = SpawnAgent(context);
            await HandleAsk(context, pid);
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

        private static Task HandleAsk(IContext context, PID pid)
        {
            AnsiConsole.WriteLine();
            var content = AnsiConsole.Ask<string>(">");
            var payload = new Agent.Ask(ChatRole.User, From: null, Content: content);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
            return Task.CompletedTask;
        }

        private static async Task RenderStreaming(Streaming response)
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
        }
    }
}
