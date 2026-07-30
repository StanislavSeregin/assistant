using Assistant.App.Interaction;
using System;

namespace Assistant.App.Actors;

/// <summary>
/// Owns the per-turn thinking stream and outbound timing clock.
/// </summary>
internal sealed class AgentTurnTimeline(IOutputEventSink output)
{
    private string _agentName = string.Empty;
    private Guid? _thinkingStreamId;
    private DateTime _runStartedAt;
    private DateTime? _lastThinkingActivityAt;

    public void Bind(string agentName) => _agentName = agentName;

    public void BeginTurn()
    {
        // Close any orphaned thinking stream before starting a new run (e.g. nudge).
        CompleteThinking();
        _runStartedAt = DateTime.Now;
        _lastThinkingActivityAt = null;
    }

    public void EmitThinking(string text)
    {
        _lastThinkingActivityAt = DateTime.Now;

        if (_thinkingStreamId is null)
        {
            _thinkingStreamId = Guid.NewGuid();
            output.Publish(new ThinkingStarted(_agentName, _thinkingStreamId.Value));
        }

        output.Publish(new ThinkingDelta(_agentName, _thinkingStreamId.Value, text));
    }

    public void CompleteThinking(long? inputTokens = null, DateTime? endedAt = null)
    {
        if (_thinkingStreamId is not { } streamId)
        {
            return;
        }

        output.Publish(new ThinkingCompleted(_agentName, streamId, inputTokens, endedAt));
        _thinkingStreamId = null;
    }

    /// <summary>
    /// Prefer attaching usage to an open thinking stream;
    /// otherwise emit a standalone usage line.
    /// </summary>
    public void PublishTurnUsage(long? inputTokens)
    {
        if (_thinkingStreamId is not null)
        {
            CompleteThinking(inputTokens);
            return;
        }

        if (inputTokens is long tokens)
        {
            output.Publish(new UsageOutput(_agentName, tokens));
        }
    }

    /// <summary>
    /// Closes any open thinking stream for an outbound message and returns its StartedAt.
    /// </summary>
    public DateTime CloseForOutbound()
    {
        var outboundStartedAt = _lastThinkingActivityAt ?? _runStartedAt;
        CompleteThinking(endedAt: outboundStartedAt);
        _lastThinkingActivityAt = null;
        return outboundStartedAt;
    }
}
