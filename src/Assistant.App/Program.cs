using Assistant.App.Actors;
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

        builder.ConfigureLogging(b => b.ClearProviders());
        builder.ConfigureServices((context, services) => services
            .Configure<Settings>(context.Configuration.GetSection("Settings"))
            .AddHostedService<BootstrapHostedService>()
            .AddSingleton(sp => new ActorSystem().WithServiceProvider(sp))
            .AddTransient<GUI.Actor>()
            .AddTransient<MainAgent.Actor>()
            .AddTransient<SubAgent.Actor>());

        var host = builder.Build();
        return host.RunAsync();
    }
}
