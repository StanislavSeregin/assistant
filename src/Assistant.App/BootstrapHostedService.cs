using Assistant.App.Actors;
using Microsoft.Extensions.Hosting;
using Proto;
using Proto.DependencyInjection;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class BootstrapHostedService(ActorSystem system) : IHostedService
{
    private PID[] Pids { get; set; } = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Pids = [.. StartActors()];
        return Task.CompletedTask;
    }

    private IEnumerable<PID> StartActors()
    {
        var userProps = system.DI().PropsFor<User.Actor>();
        yield return system.Root.Spawn(userProps);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var pid in Pids)
        {
            pid.Stop(system);
        }

        return Task.CompletedTask;
    }
}
