using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Lifecycle;

public sealed class LifecycleEventService(
    LifecycleEventChannel channel,
    ILifecycleEventHandler handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
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
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancelled the lifecycle channel read.
        }
    }
}
