using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Clients.Console;

public sealed class ConsoleGate
{
    private readonly SemaphoreSlim _semaphore = new(1);

    public void Enter() => _semaphore.Wait();

    public Task EnterAsync(CancellationToken cancellationToken) =>
        _semaphore.WaitAsync(cancellationToken);

    public void Exit() => _semaphore.Release();
}
