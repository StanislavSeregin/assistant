using Assistant.App.Mail;
using Microsoft.Agents.AI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace Assistant.App.Registry;

public enum NodeRunState
{
    Idle,
    Running,
    Disposed
}

/// <summary>
/// LLM execution surface for a node. Human nodes have no runtime instance.
/// </summary>
public sealed class LlmNodeRuntime
{
    private readonly ConcurrentQueue<MailNotice> _pendingNotices = new();
    private int _state = (int)NodeRunState.Idle;

    public LlmNodeRuntime()
    {
        WakeChannel = Channel.CreateUnbounded<WakeSignal>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    }

    public Channel<WakeSignal> WakeChannel { get; }

    public string? ContinuityHandoff { get; set; }

    /// <summary>
    /// After process restore: next wake should resume mid-turn without injecting a new wake message.
    /// </summary>
    public bool NeedsResumeTurn { get; set; }

    public bool PersistedDidHandleMail { get; set; }

    public bool PersistedDidCommitContext { get; set; }

    public AIAgent? Agent { get; private set; }

    public AgentSession? Session { get; private set; }

    public NodeRunState State => (NodeRunState)_state;

    public void BindSession(AIAgent agent, AgentSession session)
    {
        Agent = agent;
        Session = session;
    }

    public bool TryBeginRun() =>
        Interlocked.CompareExchange(
            ref _state,
            (int)NodeRunState.Running,
            (int)NodeRunState.Idle) == (int)NodeRunState.Idle;

    public void EndRun() =>
        Interlocked.CompareExchange(
            ref _state,
            (int)NodeRunState.Idle,
            (int)NodeRunState.Running);

    public void MarkDisposed()
    {
        Interlocked.Exchange(ref _state, (int)NodeRunState.Disposed);
        WakeChannel.Writer.TryComplete();
    }

    public void EnqueueMailNotice(MailNotice notice) => _pendingNotices.Enqueue(notice);

    public List<MailNotice> DrainPendingMailNotices()
    {
        var list = new List<MailNotice>();
        while (_pendingNotices.TryDequeue(out var notice))
        {
            list.Add(notice);
        }

        return list;
    }

    public void RequestWake() => WakeChannel.Writer.TryWrite(WakeSignal.Instance);
}

public sealed class WakeSignal
{
    public static WakeSignal Instance { get; } = new();
}

public sealed record MailNotice(
    string MailId,
    DateTime Timestamp,
    string From,
    string Subject,
    bool IsFromParent);
