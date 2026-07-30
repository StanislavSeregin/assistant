using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace AssistantV2.App.Lifecycle;

public sealed class LifecycleEventService(
    LifecycleEventChannel channel,
    ILifecycleEventHandler handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var lifecycleEvent in channel.ReadAllAsync(stoppingToken))
        {
            if (lifecycleEvent is LifecycleDrainBarrier barrier)
            {
                handler.CompleteWhenIdle(barrier);
                continue;
            }

            handler.Handle(lifecycleEvent);
        }
    }
}
