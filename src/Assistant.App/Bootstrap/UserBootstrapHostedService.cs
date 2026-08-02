using Assistant.App.Registry;
using Assistant.App.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Bootstrap;

/// <summary>
/// Registers the User (Human) root node and seeds the default LLM child.
/// </summary>
public sealed class UserBootstrapHostedService(
    NodeRegistry registry,
    AgentBootstrap bootstrap,
    IOptions<Settings> settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var cfg = settings.Value;
        var user = registry.RegisterUser(cfg.UserDescription);
        var child = registry.SpawnChild(
            user,
            cfg.DefaultChildName,
            cfg.DefaultChildDescription,
            cfg.DefaultChildInstructions,
            parentRole: cfg.UserDescription);
        await bootstrap.BootstrapAsync(child, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
