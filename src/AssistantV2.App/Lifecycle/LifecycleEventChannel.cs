using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace AssistantV2.App.Lifecycle;

public sealed class LifecycleEventChannel : ILifecycleSink
{
    private readonly Channel<ILifecycleEvent> _events = Channel.CreateUnbounded<ILifecycleEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public void Publish(ILifecycleEvent lifecycleEvent)
    {
        if (!_events.Writer.TryWrite(lifecycleEvent))
        {
            throw new ChannelClosedException();
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        var barrier = new LifecycleDrainBarrier();
        Publish(barrier);
        await barrier.Task.WaitAsync(cancellationToken);
    }

    public IAsyncEnumerable<ILifecycleEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);
}
