using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class AgentConcurrencyLimiter(IOptions<Settings> options)
{
    private sealed class Waiter
    {
        public TaskCompletionSource<Lease> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration CancellationRegistration { get; set; }
    }

    private sealed class Lease(AgentConcurrencyLimiter owner) : IDisposable
    {
        private enum LeaseState
        {
            Held,
            Released,
            Reacquiring,
            Disposed
        }

        private readonly object _sync = new();
        private TaskCompletionSource? _resumeCompletion;
        private CancellationToken _reacquireCancellationToken;
        private int _suspensions;
        private LeaseState _state = LeaseState.Held;

        public async Task<T> WhileReleasedAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken)
        {
            Task resumeTask;
            var release = false;
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_state == LeaseState.Disposed, this);
                _suspensions++;
                if (_state == LeaseState.Held)
                {
                    _state = LeaseState.Released;
                    _resumeCompletion = new TaskCompletionSource(
                        TaskCreationOptions.RunContinuationsAsynchronously);
                    _reacquireCancellationToken = cancellationToken;
                    release = true;
                }

                resumeTask = _resumeCompletion!.Task;
            }

            if (release)
            {
                owner.Release();
            }

            Exception? error = null;
            T? result = default;
            try
            {
                result = await action();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            var startReacquire = false;
            lock (_sync)
            {
                _suspensions--;
                if (_suspensions == 0 && _state == LeaseState.Released)
                {
                    _state = LeaseState.Reacquiring;
                    startReacquire = true;
                }
            }

            if (startReacquire)
            {
                _ = ReacquireAsync(_reacquireCancellationToken);
            }

            await resumeTask;
            if (error is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            }

            return result!;
        }

        public void Dispose()
        {
            var release = false;
            lock (_sync)
            {
                if (_state == LeaseState.Disposed)
                {
                    return;
                }

                release = _state == LeaseState.Held;
                _state = LeaseState.Disposed;
                if (!release)
                {
                    _resumeCompletion?.TrySetException(
                        new ObjectDisposedException(nameof(Lease)));
                }
            }

            if (release)
            {
                owner.Release();
            }
        }

        private void Detach()
        {
            lock (_sync)
            {
                _state = LeaseState.Disposed;
            }
        }

        private async Task ReacquireAsync(CancellationToken cancellationToken)
        {
            Lease? reacquired = null;
            try
            {
                reacquired = await owner.AcquireAsync(cancellationToken);
                var keep = false;
                lock (_sync)
                {
                    if (_state == LeaseState.Disposed)
                    {
                        // The outer model run already unwound. Release below.
                    }
                    else if (_suspensions == 0)
                    {
                        _state = LeaseState.Held;
                        keep = true;
                        _resumeCompletion!.TrySetResult();
                    }
                    else
                    {
                        // A late parallel tool wait started while reacquiring.
                        // Return this slot and let the last waiter reacquire again.
                        _state = LeaseState.Released;
                    }
                }

                if (keep)
                {
                    reacquired.Detach();
                    reacquired = null;
                }
            }
            catch (Exception ex)
            {
                lock (_sync)
                {
                    _state = LeaseState.Disposed;
                    _resumeCompletion?.TrySetException(ex);
                }
            }
            finally
            {
                reacquired?.Dispose();
            }
        }
    }

    private readonly object _sync = new();
    private readonly Queue<Waiter> _waiters = [];
    private readonly AsyncLocal<Lease?> _currentLease = new();
    private readonly int _capacity = Math.Max(1, options.Value.MaxConcurrentAgentRuns);
    private int _active;

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        using var lease = await AcquireAsync(cancellationToken);
        var previous = _currentLease.Value;
        _currentLease.Value = lease;
        try
        {
            return await action();
        }
        finally
        {
            _currentLease.Value = previous;
        }
    }

    public Task RunAsync(Func<Task> action, CancellationToken cancellationToken) =>
        RunAsync(async () =>
        {
            await action();
            return true;
        }, cancellationToken);

    /// <summary>
    /// Fairly yields the current model lease while a tool waits for another agent.
    /// Parallel waits from the same model run share one suspension.
    /// </summary>
    public Task<T> WhileReleasedAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        var lease = _currentLease.Value
            ?? throw new InvalidOperationException(
                $"{nameof(WhileReleasedAsync)} must be called from inside {nameof(RunAsync)}.");
        return lease.WhileReleasedAsync(action, cancellationToken);
    }

    private Task<Lease> AcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (_active < _capacity && _waiters.Count == 0)
            {
                _active++;
                return Task.FromResult(new Lease(this));
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

    private void Release()
    {
        List<(Waiter Waiter, Lease Lease)> granted = [];
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
