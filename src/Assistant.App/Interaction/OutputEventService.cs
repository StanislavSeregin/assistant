using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Interaction;

public sealed class OutputEventService(
    OutputEventChannel channel,
    IOutputEventHandler handler) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var outputEvent in channel.ReadAllAsync(stoppingToken))
        {
            if (outputEvent is OutputDrainBarrier barrier)
            {
                barrier.Complete();
                continue;
            }

            handler.Handle(outputEvent);
        }
    }
}
