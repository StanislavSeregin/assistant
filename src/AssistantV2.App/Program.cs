using AssistantV2.App.Bootstrap;
using AssistantV2.App.Lifecycle;
using AssistantV2.App.Mail;
using AssistantV2.App.Registry;
using AssistantV2.App.Runtime;
using AssistantV2.App.Smoke;
using AssistantV2.App.Support;
using AssistantV2.App.Tools;
using AssistantV2.App.UI.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AssistantV2.App;

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
            .ConfigureServices((context, services) => services
                .Configure<Settings>(context.Configuration.GetSection("Settings"))
                .AddSingleton<ChatClientFactory>()
                .AddSingleton<LifecycleEventChannel>()
                .AddSingleton<ILifecycleSink>(sp => sp.GetRequiredService<LifecycleEventChannel>())
                .AddSingleton<ILifecycleEventHandler, SpectreLifecycleOutput>()
                .AddSingleton<ConsoleGate>()
                .AddSingleton<IUserInput, SpectreUserInput>()
                .AddSingleton<AgentRegistry>()
                .AddSingleton<MailService>()
                .AddSingleton<ModelSlotLimiter>()
                .AddSingleton<AgentBootstrap>()
                .AddSingleton<AgentMailTools>()
                .AddSingleton<StatelessAgent>()
                .AddSingleton<MailTurnSupport>()
                .AddSingleton<TurnRunner>()
                .AddHostedService<LifecycleEventService>()
                .AddHostedService<RootBootstrapHostedService>()
                .AddHostedService<AgentScheduler>()
                .AddHostedService<ConsoleUserBridge>());

        var host = builder.Build();
        // Banner before hosted services so AgentSpawned etc. cannot flash then get cleared.
        host.Services.GetRequiredService<IUserInput>().ShowBanner();
        await host.RunAsync();
    }
}
