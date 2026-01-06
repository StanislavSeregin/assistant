using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class UIHostedService(IObservable<KekMessage?> kekMessageObservable) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AnsiConsole.Write(new FigletText("Assistant")
        {
            Justification = Justify.Center,
            Color = ConsoleColor.Cyan
        });

        kekMessageObservable.Subscribe(msg =>
        {
            AnsiConsole.MarkupLine($"[green]✓ {msg?.Text} [/]");
        });

        return Task.CompletedTask;
    }
}
