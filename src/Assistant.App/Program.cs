using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Assistant.App;

public static class Program
{
    public static Task Main(string[] args)
    {
        var builder = Host.CreateDefaultBuilder(args);
        builder.ConfigureLogging(b => b.ClearProviders());
        builder.ConfigureServices(services => services
            .AddMessages()
            .AddHostedService<UIHostedService>()
            .AddHostedService<AIHostedService>());

        var host = builder.Build();
        return host.RunAsync();
    }
}
