using Assistant.App.Registry;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Assistant.App.Runtime;

/// <summary>
/// Busy row for a User-direct child: spinner + optional actor (descendant or sideband).
/// <see cref="ActiveActor"/> is null when idle or when the root itself is the only worker.
/// </summary>
public readonly record struct AgentRowActivity(bool IsBusy, string? ActiveActor)
{
    public static AgentRowActivity Idle { get; } = new(false, null);
}

/// <summary>Well-known sideband actors (not graph nodes).</summary>
public static class ModelActivityActors
{
    public const string Support = "support";
}

/// <summary>
/// Live model users for the Agents list: graph turns (<see cref="NodeRunState.Running"/>)
/// plus ephemeral sideband work (support, …) attributed to a turn-owner node.
/// </summary>
public sealed class ModelActivityTracker(NodeRegistry registry)
{
    private readonly ConcurrentDictionary<string, int> _sideband = new(StringComparer.Ordinal);
    private readonly Lock _raiseGate = new();

    public event EventHandler? Changed;

    public bool AnyBusy
    {
        get
        {
            if (!_sideband.IsEmpty)
            {
                return true;
            }

            foreach (var node in registry.All())
            {
                if (!node.IsDisposed && node.Llm?.State == NodeRunState.Running)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Attribute non-node model work to <paramref name="turnOwner"/> (the agent whose
    /// turn context is running — may be a nested child). UI maps it up to a User row.
    /// </summary>
    public IDisposable Enter(string turnOwner, string actor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(turnOwner);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var key = SidebandKey(turnOwner.Trim(), actor.Trim());
        _sideband.AddOrUpdate(key, 1, static (_, n) => n + 1);
        RaiseChanged();
        return new SidebandLease(this, key);
    }

    /// <summary>Wake listeners after graph turn boundaries (Running already updated).</summary>
    public void Pulse() => RaiseChanged();

    public AgentRowActivity ForUserChild(string rootName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootName);

        var user = registry.User;
        if (user is null
            || !user.ChildrenByName.TryGetValue(rootName, out var rootId)
            || !registry.TryGet(rootId, out var root)
            || root.IsDisposed)
        {
            return AgentRowActivity.Idle;
        }

        if (TryPickSidebandActor(root, out var sideband))
        {
            return new AgentRowActivity(true, sideband);
        }

        var running = CollectRunning(root);
        if (running.Count == 0)
        {
            return AgentRowActivity.Idle;
        }

        var actor = PickGraphActor(root, running);
        return new AgentRowActivity(true, actor);
    }

    private bool TryPickSidebandActor(NodeHandle root, out string actor)
    {
        actor = null!;
        string? found = null;
        foreach (var key in _sideband.Keys)
        {
            if (!TryParseSidebandKey(key, out var owner, out var label))
            {
                continue;
            }

            if (!BelongsUnder(root, owner))
            {
                continue;
            }

            // Prefer support label; otherwise first match (stable enough for tiny trees).
            if (string.Equals(label, ModelActivityActors.Support, StringComparison.Ordinal))
            {
                actor = label;
                return true;
            }

            found ??= label;
        }

        if (found is null)
        {
            return false;
        }

        actor = found;
        return true;
    }

    private List<NodeHandle> CollectRunning(NodeHandle root)
    {
        var list = new List<NodeHandle>();
        CollectRunningCore(root, list);
        return list;
    }

    private void CollectRunningCore(NodeHandle node, List<NodeHandle> sink)
    {
        if (node.IsDisposed)
        {
            return;
        }

        if (node.Llm?.State == NodeRunState.Running)
        {
            sink.Add(node);
        }

        foreach (var child in registry.ListChildren(node))
        {
            CollectRunningCore(child, sink);
        }
    }

    /// <summary>
    /// Deepest busy descendant name; null if only <paramref name="root"/> is running.
    /// </summary>
    private string? PickGraphActor(NodeHandle root, List<NodeHandle> running)
    {
        NodeHandle? best = null;
        var bestDepth = -1;
        foreach (var node in running)
        {
            if (node.Id == root.Id)
            {
                continue;
            }

            var depth = DepthFromRoot(root, node);
            if (depth < 0)
            {
                continue;
            }

            if (depth > bestDepth
                || (depth == bestDepth
                    && best is not null
                    && string.CompareOrdinal(node.Name, best.Name) < 0))
            {
                best = node;
                bestDepth = depth;
            }
        }

        return best?.Name;
    }

    private int DepthFromRoot(NodeHandle root, NodeHandle node)
    {
        var depth = 0;
        var current = node;
        while (current.Id != root.Id)
        {
            if (current.ParentId is null || !registry.TryGet(current.ParentId.Value, out var parent))
            {
                return -1;
            }

            depth++;
            current = parent;
        }

        return depth;
    }

    private bool BelongsUnder(NodeHandle root, string nodeName)
    {
        if (string.Equals(root.Name, nodeName, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var node in registry.All())
        {
            if (node.IsDisposed || !string.Equals(node.Name, nodeName, StringComparison.Ordinal))
            {
                continue;
            }

            return DepthFromRoot(root, node) >= 0;
        }

        return false;
    }

    private void ReleaseSideband(string key)
    {
        while (true)
        {
            if (!_sideband.TryGetValue(key, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                if (_sideband.TryRemove(new KeyValuePair<string, int>(key, count)))
                {
                    RaiseChanged();
                    return;
                }

                continue;
            }

            if (_sideband.TryUpdate(key, count - 1, count))
            {
                RaiseChanged();
                return;
            }
        }
    }

    private void RaiseChanged()
    {
        lock (_raiseGate)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string SidebandKey(string turnOwner, string actor) =>
        string.Concat(turnOwner, "\0", actor);

    private static bool TryParseSidebandKey(string key, out string turnOwner, out string actor)
    {
        var split = key.IndexOf('\0');
        if (split <= 0 || split >= key.Length - 1)
        {
            turnOwner = "";
            actor = "";
            return false;
        }

        turnOwner = key[..split];
        actor = key[(split + 1)..];
        return true;
    }

    private sealed class SidebandLease(ModelActivityTracker owner, string key) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            owner.ReleaseSideband(key);
        }
    }
}
