using Assistant.App.Registry;
using Assistant.App.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Bootstrap;

public sealed class RootBootstrapHostedService(
    AgentRegistry registry,
    AgentBootstrap bootstrap,
    IOptions<Settings> settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = settings.Value;
        var root = registry.RegisterRoot(
            cfg.RootName,
            cfg.RootDescription,
            cfg.RootInstructions,
            cfg.UserDescription);
        await bootstrap.BootstrapAsync(root, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
