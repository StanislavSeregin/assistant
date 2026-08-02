using Assistant.App.Mail;
using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Assistant.App.Registry;

public enum NodeRuntimeKind
{
    Human,
    Llm
}

/// <summary>
/// Graph participant: identity, parent/children, inbox. Execution strategy is <see cref="RuntimeKind"/>.
/// </summary>
public sealed class NodeHandle
{
    private int _disposed;

    public NodeHandle(
        NodeId id,
        string name,
        string description,
        string instructions,
        NodeId? parentId,
        string? parentDescription,
        NodeRuntimeKind runtimeKind)
    {
        Id = id;
        Name = name;
        Description = description;
        Instructions = instructions;
        ParentId = parentId;
        ParentDescription = parentDescription;
        RuntimeKind = runtimeKind;
        Inbox = new MailInbox();
        Llm = runtimeKind == NodeRuntimeKind.Llm ? new LlmNodeRuntime() : null;
        Lifetime = new CancellationTokenSource();
    }

    public NodeId Id { get; }

    public string Name { get; }

    public string Description { get; }

    public string Instructions { get; }

    /// <summary>Null for the root User node.</summary>
    public NodeId? ParentId { get; }

    public string? ParentDescription { get; }

    public NodeRuntimeKind RuntimeKind { get; }

    public ConcurrentDictionary<string, NodeId> ChildrenByName { get; } = new(StringComparer.Ordinal);

    public MailInbox Inbox { get; }

    public LlmNodeRuntime? Llm { get; }

    public CancellationTokenSource Lifetime { get; }

    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public NodeRunState State
    {
        get
        {
            if (IsDisposed)
            {
                return NodeRunState.Disposed;
            }

            return Llm?.State ?? NodeRunState.Idle;
        }
    }

    public void MarkDisposed()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Lifetime.Cancel();
        Llm?.MarkDisposed();
    }
}
