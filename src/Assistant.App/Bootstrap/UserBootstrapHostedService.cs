using Assistant.App.Registry;
using Assistant.App.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Bootstrap;

/// <summary>
/// Registers the User root node and optionally seeds Settings.AgentTemplates[0].
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

        if (cfg.AgentTemplates is not { Count: > 0 })
        {
            return;
        }

        var template = cfg.AgentTemplates[0];
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return;
        }

        var child = registry.SpawnChild(
            user,
            template.Name.Trim(),
            template.Description,
            template.Instructions,
            parentRole: cfg.UserDescription);
        await bootstrap.BootstrapAsync(child, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
