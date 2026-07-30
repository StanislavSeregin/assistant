using Assistant.App.Interaction;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal sealed class AgentMessagePublisher(IOutputEventSink output, AgentTurnTimeline timeline)
{
    private string _agentName = string.Empty;
    private Func<Guid?> _transactionId = static () => null;

    public void Bind(string agentName, Func<Guid?> transactionId)
    {
        _agentName = agentName;
        _transactionId = transactionId;
    }

    public void PublishOutbound(
        string to,
        string body,
        MessageKind kind,
        string? requestId = null)
    {
        var outboundStartedAt = timeline.CloseForOutbound();
        var streamId = Guid.NewGuid();
        output.Publish(new MessageStarted(
            _agentName,
            to,
            streamId,
            IsForUser: to == "User",
            StartedAt: outboundStartedAt,
            _transactionId(),
            requestId,
            kind));
        output.Publish(new MessageDelta(streamId, body));
        output.Publish(new MessageCompleted(streamId));
    }

    public Task DrainAsync(CancellationToken cancellationToken = default) =>
        output.DrainAsync(cancellationToken);
}
