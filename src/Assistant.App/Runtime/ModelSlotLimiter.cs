using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

public sealed class ModelSlotLimiter(IOptions<Settings> options)
{
    private sealed class Waiter
    {
        public TaskCompletionSource<IDisposable> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }

    private sealed class Lease(ModelSlotLimiter owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.Release();
        }
    }

    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = [];
    private readonly int _capacity = Math.Max(1, options.Value.MaxConcurrentAgentRuns);
    private int _active;

    public Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_active < _capacity && _waiters.Count == 0)
            {
                _active++;
                return Task.FromResult<IDisposable>(new Lease(this));
            }

            var waiter = new Waiter();
            _waiters.Enqueue(waiter);
            if (cancellationToken.CanBeCanceled)
            {
                waiter.CancellationRegistration = cancellationToken.Register(
                    static state => ((Waiter)state!).Completion.TrySetCanceled(),
                    waiter);
            }

            return waiter.Completion.Task;
        }
    }

    public async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        using var lease = await AcquireAsync(cancellationToken);
        await action();
    }

    private void Release()
    {
        List<(Waiter Waiter, IDisposable Lease)> granted = [];
        lock (_sync)
        {
            _active--;
            while (_active < _capacity && _waiters.TryDequeue(out var waiter))
            {
                if (waiter.Completion.Task.IsCompleted)
                {
                    waiter.CancellationRegistration.Dispose();
                    continue;
                }

                _active++;
                granted.Add((waiter, new Lease(this)));
            }
        }

        foreach (var (waiter, lease) in granted)
        {
            waiter.CancellationRegistration.Dispose();
            if (!waiter.Completion.TrySetResult(lease))
            {
                lease.Dispose();
            }
        }
    }
}
