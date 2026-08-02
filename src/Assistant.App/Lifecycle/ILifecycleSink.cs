using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Lifecycle;

public interface ILifecycleSink
{
    void Publish(ILifecycleEvent lifecycleEvent);

    Task DrainAsync(CancellationToken cancellationToken = default);
}
