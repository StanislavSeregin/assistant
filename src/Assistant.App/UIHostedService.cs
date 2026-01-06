using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class UIHostedService(
    ISubject<IUIEvent> uiEventSubject,
    IObservable<IAIEvent?> aiEventObservable
) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.InputEncoding = Encoding.UTF8;
        AnsiConsole.Write(new FigletText("Assistant")
        {
            Justification = Justify.Center,
            Color = ConsoleColor.Cyan
        });

        HandleRequest();
        aiEventObservable.Subscribe(async aiEvent =>
        {
            await (aiEvent switch
            {
                StreamingAIResponse msg => HandleMessage(msg),
                _ => Task.CompletedTask
            });
        });

        return Task.CompletedTask;
    }

    private void HandleRequest()
    {
        AnsiConsole.WriteLine();
        var request = AnsiConsole.Ask<string>(">");
        uiEventSubject.OnNext(new HumanMessage(request));
    }

    private async Task HandleMessage(StreamingAIResponse streamingMessage)
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
        HandleRequest();
    }
}
