using Microsoft.Agents.AI;
using Assistant.App.Checklist;
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

public static class SystemNotificationKinds
{
    public const string Mail = "mail";
}

/// <summary>
/// Mid-turn system event queued while a node is Running; drained into one ChatMessage on the next model call.
/// </summary>
/// <param name="ItemId">Optional stable id (e.g. mail id) so wake can drop notices already listed in the turn opener.</param>
public sealed record SystemNotification(string Kind, string Detail, string? ItemId = null);

/// <summary>
/// LLM execution surface for a node. Human nodes have no runtime instance.
/// </summary>
public sealed class LlmNodeRuntime
{
    private readonly ConcurrentQueue<SystemNotification> _pendingNotifications = new();
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

    /// <summary>Engine-owned open work plan; survives CommitContext history clear.</summary>
    public AgentChecklist Checklist { get; } = new();

    /// <summary>
    /// After process restore: next wake should resume mid-turn without injecting a new wake message.
    /// </summary>
    public bool NeedsResumeTurn { get; set; }

    public bool PersistedDidHandleMail { get; set; }

    public bool PersistedDidMutateChecklist { get; set; }

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

    public void EnqueueSystemNotification(SystemNotification notice) =>
        _pendingNotifications.Enqueue(notice);

    public List<SystemNotification> DrainPendingSystemNotifications()
    {
        var list = new List<SystemNotification>();
        while (_pendingNotifications.TryDequeue(out var notice))
        {
            list.Add(notice);
        }

        return list;
    }

    /// <summary>
    /// Drops pending notices matching <paramref name="shouldDiscard"/>; keeps the rest in order.
    /// </summary>
    public void DiscardPendingSystemNotifications(Func<SystemNotification, bool> shouldDiscard)
    {
        var keep = new List<SystemNotification>();
        while (_pendingNotifications.TryDequeue(out var notice))
        {
            if (!shouldDiscard(notice))
            {
                keep.Add(notice);
            }
        }

        foreach (var notice in keep)
        {
            _pendingNotifications.Enqueue(notice);
        }
    }

    public void RequestWake() => WakeChannel.Writer.TryWrite(WakeSignal.Instance);
}

public sealed class WakeSignal
{
    public static WakeSignal Instance { get; } = new();
}
