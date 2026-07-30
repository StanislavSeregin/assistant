using Assistant.App.Actors;
using Assistant.App.Clients.Console;
using Assistant.App.Interaction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.DependencyInjection;
using System.Threading.Tasks;

namespace Assistant.App;

public class Program
{
    public static Task Main(string[] args)
    {
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
                .AddSingleton<AgentConcurrencyLimiter>()
                .AddSingleton<AgentSnapshotCompactor>()
                .AddSingleton<ConsoleGate>()
                .AddSingleton<IUserInput, SpectreUserInput>()
                .AddSingleton<OutputEventChannel>()
                .AddSingleton<IOutputEventSink>(sp => sp.GetRequiredService<OutputEventChannel>())
                .AddSingleton<IOutputEventHandler, SpectreConsoleOutput>()
                .AddSingleton(sp => new ActorSystem().WithServiceProvider(sp))
                .AddTransient<User.Actor>()
                .AddTransient<Agent.Actor>()
                .AddHostedService<OutputEventService>()
                .AddHostedService<BootstrapHostedService>());

        var host = builder.Build();
        return host.RunAsync();
    }
}
