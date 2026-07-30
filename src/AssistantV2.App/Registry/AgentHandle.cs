using AssistantV2.App.Mail;
using Microsoft.Agents.AI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace AssistantV2.App.Registry;

public enum AgentRunState
{
    Idle,
    Running,
    Disposed
}

public sealed class AgentHandle
{
    private readonly ConcurrentQueue<MailNotice> _pendingNotices = new();
    private int _state = (int)AgentRunState.Idle;

    public AgentHandle(
        AgentId id,
        string name,
        string description,
        string instructions,
        AgentId parentId,
        string? parentDescription)
    {
        Id = id;
        Name = name;
        Description = description;
        Instructions = instructions;
        ParentId = parentId;
        ParentDescription = parentDescription;
        Inbox = new MailInbox();
        WakeChannel = Channel.CreateUnbounded<WakeSignal>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false
            });
    }

    public AgentId Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Instructions { get; }

    public AgentId ParentId { get; }

    public string? ParentDescription { get; }

    public ConcurrentDictionary<string, AgentId> ChildrenByName { get; } = new(StringComparer.Ordinal);

    public MailInbox Inbox { get; }

    public Channel<WakeSignal> WakeChannel { get; }

    public AIAgent? Agent { get; private set; }

    public AgentSession? Session { get; private set; }

    public CancellationTokenSource Lifetime { get; } = new();

    public AgentRunState State => (AgentRunState)_state;

    public void BindSession(AIAgent agent, AgentSession session)
    {
        Agent = agent;
        Session = session;
    }

    public bool TryBeginRun() =>
        Interlocked.CompareExchange(
            ref _state,
            (int)AgentRunState.Running,
            (int)AgentRunState.Idle) == (int)AgentRunState.Idle;

    public void EndRun() =>
        Interlocked.CompareExchange(
            ref _state,
            (int)AgentRunState.Idle,
            (int)AgentRunState.Running);

    public void MarkDisposed()
    {
        Interlocked.Exchange(ref _state, (int)AgentRunState.Disposed);
        Lifetime.Cancel();
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
