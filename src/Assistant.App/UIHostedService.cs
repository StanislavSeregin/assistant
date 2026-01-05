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
        AnsiConsole.MarkupLine("[green]✓ Build completed successfully[/]");

        kekMessageObservable.Subscribe(msg =>
        {
            AnsiConsole.MarkupLine($"[green]✓ {msg?.Text} [/]");
        });

        return Task.CompletedTask;
    }
}
