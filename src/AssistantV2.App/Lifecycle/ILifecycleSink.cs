using System.Threading;
using System.Threading.Tasks;

namespace AssistantV2.App.Lifecycle;

public interface ILifecycleSink
{
    void Publish(ILifecycleEvent lifecycleEvent);

    Task DrainAsync(CancellationToken cancellationToken = default);
}
