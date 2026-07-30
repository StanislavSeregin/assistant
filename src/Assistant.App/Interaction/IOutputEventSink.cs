using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Interaction;

public interface IOutputEventSink
{
    void Publish(IOutputEvent outputEvent);

    /// <summary>
    /// Wait until every event published so far has been handled by the UI pump.
    /// </summary>
    Task DrainAsync(CancellationToken cancellationToken = default);
}
