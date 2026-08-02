using Assistant.App.Smoke;
using Assistant.App.UI;
using Assistant.App.UI.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App;

public class Program
{
    public static async Task Main(string[] args)
    {
        ConsoleUtf8.Enable();

        if (args.Any(a => string.Equals(a, "--smoke", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = await MailRegistrySmoke.RunAsync();
            return;
        }

        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureAppConfiguration((context, config) =>
        {
            if (context.HostingEnvironment.IsDevelopment())
            {
                config.AddUserSecrets<Program>();
            }
        });

        builder
            .ConfigureLogging(b => b.ClearProviders())
            .ConfigureServices((context, services) =>
            {
                services.AddAssistantCore(context.Configuration);
                services.AddTerminalGuiUi();
            });

        var host = builder.Build();
        await host.StartAsync();
        try
        {
            host.Services.GetRequiredService<IAppUi>().Run();
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
