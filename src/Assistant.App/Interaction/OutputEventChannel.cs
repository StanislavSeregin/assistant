using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Channels;

namespace Assistant.App.Interaction;

public sealed class OutputEventChannel : IOutputEventSink
{
    private readonly Channel<IOutputEvent> _events = Channel.CreateUnbounded<IOutputEvent>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    public void Publish(IOutputEvent outputEvent)
    {
        if (!_events.Writer.TryWrite(outputEvent))
        {
            throw new ChannelClosedException();
        }
    }

    public async Task DrainAsync(CancellationToken cancellationToken = default)
    {
        var barrier = new OutputDrainBarrier();
        Publish(barrier);
        await barrier.Task.WaitAsync(cancellationToken);
    }

    public IAsyncEnumerable<IOutputEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _events.Reader.ReadAllAsync(cancellationToken);
}
