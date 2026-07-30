using Microsoft.Extensions.Options;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class AgentConcurrencyLimiter(IOptions<Settings> options)
{
    private readonly SemaphoreSlim _semaphore = new(Math.Max(1, options.Value.MaxConcurrentAgentRuns));

    public async Task RunAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            return await action();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Releases the LLM slot while awaiting another agent (needed when MaxConcurrentAgentRuns = 1).
    /// </summary>
    public async Task<T> WhileReleasedAsync<T>(Func<Task<T>> action)
    {
        _semaphore.Release();
        try
        {
            return await action();
        }
        finally
        {
            await _semaphore.WaitAsync(CancellationToken.None);
        }
    }
}
