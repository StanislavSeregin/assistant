using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class UIHostedService(
    IObservable<IMessage?> kekMessageObservable
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AnsiConsole.Write(new FigletText("Assistant")
        {
            Justification = Justify.Center,
            Color = ConsoleColor.Cyan
        });

        kekMessageObservable.Subscribe(async message =>
        {
            await (message switch
            {
                StreamingMessage msg => HandleMessage(msg),
                _ => Task.CompletedTask
            });
        });

        return Task.CompletedTask;
    }

    private static async Task HandleMessage(StreamingMessage streamingMessage)
    {
        var startTime = DateTime.Now;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[cyan]{streamingMessage.Name}[/] [grey][[{startTime:HH:mm:ss}]][/]").LeftJustified());
        await foreach (var text in streamingMessage.LiveContent)
        {
            AnsiConsole.Markup($"[yellow]{Markup.Escape(text)}[/]");
        }

        var endTime = DateTime.Now;
        var duration = endTime - startTime;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[grey][[{endTime:HH:mm:ss}]] (took {duration.TotalSeconds:F1}s)[/]").RightJustified());
    }
}
