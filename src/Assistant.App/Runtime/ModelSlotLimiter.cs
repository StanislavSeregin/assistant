using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

/// <summary>
/// Limits concurrent model runs across agents.
/// </summary>
public sealed class ModelSlotLimiter : IDisposable
{
    private readonly SemaphoreSlim _slots;

    public ModelSlotLimiter(IOptions<Settings> options)
    {
        var capacity = Math.Max(1, options.Value.MaxConcurrentAgentRuns);
        _slots = new SemaphoreSlim(capacity, capacity);
    }

    public async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _slots.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _slots.Release();
        }
    }

    public void Dispose() => _slots.Dispose();
}
