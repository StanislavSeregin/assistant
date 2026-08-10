using Assistant.App.Checklist;
using Assistant.App.Lifecycle;
using Assistant.App.Persistence;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Assistant.App.Registry;

public sealed class NodeRegistry(ILifecycleSink lifecycle, IAgentStateStore store)
{
    private readonly ConcurrentDictionary<NodeId, NodeHandle> _nodes = new();
    private readonly Channel<NodeHandle> _llmRegistered = Channel.CreateUnbounded<NodeHandle>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });

    private bool _userRegistered;

    public NodeHandle? User =>
        _nodes.TryGetValue(NodeId.User, out var handle) ? handle : null;

    /// <summary>LLM nodes only — consumed by the turn scheduler.</summary>
    public ChannelReader<NodeHandle> LlmRegistered => _llmRegistered.Reader;

    public bool TryGet(NodeId id, out NodeHandle handle) => _nodes.TryGetValue(id, out handle!);

    public NodeHandle RegisterUser(string description)
    {
        if (_userRegistered)
        {
            throw new InvalidOperationException("User node is already registered.");
        }

        var handle = new NodeHandle(
            NodeId.User,
            NodeId.User.Value,
            description.Trim(),
            instructions: string.Empty,
            parentId: null,
            parentDescription: null,
            NodeRuntimeKind.Human);

        if (!_nodes.TryAdd(NodeId.User, handle))
        {
            throw new InvalidOperationException("User node already exists.");
        }

        _userRegistered = true;
        lifecycle.Publish(new NodeSpawned(
            handle.Name,
            handle.Id.Value,
            Parent: string.Empty,
            handle.Description,
            handle.Instructions,
            ParentDescription: null));
        store.SaveNode(handle);
        return handle;
    }

    /// <summary>Restores a node from LiteDB without re-seeding templates.</summary>
    public NodeHandle RestoreNode(PersistedNodeDocument doc)
    {
        var id = new NodeId(doc.Id);
        if (id.IsUser)
        {
            if (_userRegistered)
            {
                throw new InvalidOperationException("User node is already registered.");
            }

            var user = new NodeHandle(
                NodeId.User,
                NodeId.User.Value,
                doc.Description,
                instructions: string.Empty,
                parentId: null,
                parentDescription: null,
                NodeRuntimeKind.Human);

            if (!_nodes.TryAdd(NodeId.User, user))
            {
                throw new InvalidOperationException("User node already exists.");
            }

            _userRegistered = true;
            foreach (var (childName, childId) in doc.ChildrenByName)
            {
                user.ChildrenByName[childName] = new NodeId(childId);
            }

            lifecycle.Publish(new NodeSpawned(
                user.Name,
                user.Id.Value,
                Parent: string.Empty,
                user.Description,
                user.Instructions,
                ParentDescription: null));
            return user;
        }

        var parentId = doc.ParentId is null ? (NodeId?)null : new NodeId(doc.ParentId);
        var handle = new NodeHandle(
            id,
            doc.Name,
            doc.Description,
            doc.Instructions,
            parentId,
            doc.ParentDescription,
            doc.RuntimeKind);

        if (handle.Llm is not null)
        {
            handle.Llm.ContinuityHandoff = doc.ContinuityHandoff;
            handle.Llm.PersistedDidHandleMail = doc.DidHandleMail;
            handle.Llm.PersistedDidMutateChecklist = doc.DidMutateChecklist;
            handle.Llm.PersistedDidCommitContext = doc.DidCommitContext;
            ChecklistPersistence.ApplyPersisted(handle.Llm.Checklist, doc.Checklist);
        }

        foreach (var (childName, childId) in doc.ChildrenByName)
        {
            handle.ChildrenByName[childName] = new NodeId(childId);
        }

        if (!_nodes.TryAdd(id, handle))
        {
            throw new InvalidOperationException($"Node id '{id}' already exists.");
        }

        lifecycle.Publish(new NodeSpawned(
            handle.Name,
            handle.Id.Value,
            parentId?.Value ?? string.Empty,
            handle.Description,
            handle.Instructions,
            handle.ParentDescription));

        if (handle.RuntimeKind == NodeRuntimeKind.Llm)
        {
            _llmRegistered.Writer.TryWrite(handle);
        }

        return handle;
    }

    public NodeHandle SpawnChild(
        NodeHandle parent,
        string name,
        string description,
        string instructions,
        string parentRole)
    {
        if (parent.IsDisposed)
        {
            throw new InvalidOperationException($"Parent '{parent.Name}' is disposed.");
        }

        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is empty.", nameof(name));
        }

        if (string.Equals(name, NodeId.User.Value, StringComparison.Ordinal)
            || string.Equals(name, parent.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' is reserved.");
        }

        if (parent.ChildrenByName.ContainsKey(name))
        {
            throw new InvalidOperationException($"Child '{name}' already exists.");
        }

        var id = new NodeId($"{parent.Name}/{name}");
        var handle = new NodeHandle(
            id,
            name,
            description.Trim(),
            instructions.Trim(),
            parent.Id,
            string.IsNullOrWhiteSpace(parentRole) ? parent.Description : parentRole.Trim(),
            NodeRuntimeKind.Llm);

        if (!_nodes.TryAdd(id, handle))
        {
            throw new InvalidOperationException($"Node id '{id}' already exists.");
        }

        if (!parent.ChildrenByName.TryAdd(name, id))
        {
            _nodes.TryRemove(id, out _);
            throw new InvalidOperationException($"Child '{name}' already exists.");
        }

        lifecycle.Publish(new NodeSpawned(
            name,
            id.Value,
            parent.Name,
            handle.Description,
            handle.Instructions,
            handle.ParentDescription));
        store.SaveNode(handle);
        store.SaveNode(parent);
        _llmRegistered.Writer.TryWrite(handle);
        return handle;
    }

    public IReadOnlyList<NodeHandle> DisposeSubtree(NodeHandle parent, string childName)
    {
        if (parent.Id.IsUser == false && parent.IsDisposed)
        {
            throw new InvalidOperationException($"Parent '{parent.Name}' is disposed.");
        }

        childName = childName.Trim();
        if (!parent.ChildrenByName.TryRemove(childName, out var childId)
            || !_nodes.TryGetValue(childId, out var child))
        {
            throw new InvalidOperationException($"Child '{childName}' not found.");
        }

        var disposed = new List<NodeHandle>();
        DisposeRecursive(child, disposed);
        store.CommitSubtreeDisposal(parent, disposed.Select(d => d.Id.Value).ToArray());
        return disposed;
    }

    public IEnumerable<NodeHandle> All() => _nodes.Values;

    public IReadOnlyList<NodeHandle> ListChildren(NodeHandle parent)
    {
        var list = new List<NodeHandle>();
        foreach (var (_, childId) in parent.ChildrenByName.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (_nodes.TryGetValue(childId, out var child) && !child.IsDisposed)
            {
                list.Add(child);
            }
        }

        return list;
    }

    public bool IsBusy()
    {
        foreach (var node in _nodes.Values)
        {
            if (node.RuntimeKind != NodeRuntimeKind.Llm || node.IsDisposed || node.Llm is null)
            {
                continue;
            }

            if (node.Llm.State == NodeRunState.Running || node.Inbox.HasMail())
            {
                return true;
            }
        }

        return false;
    }

    public async Task WaitUntilQuietAsync(CancellationToken cancellationToken)
    {
        var idleRounds = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsBusy())
            {
                idleRounds = 0;
            }
            else
            {
                idleRounds++;
                if (idleRounds >= 2)
                {
                    return;
                }
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private void DisposeRecursive(NodeHandle handle, List<NodeHandle> disposed)
    {
        foreach (var childName in handle.ChildrenByName.Keys.ToArray())
        {
            if (handle.ChildrenByName.TryRemove(childName, out var childId)
                && _nodes.TryGetValue(childId, out var child))
            {
                DisposeRecursive(child, disposed);
            }
        }

        handle.MarkDisposed();
        _nodes.TryRemove(handle.Id, out _);
        disposed.Add(handle);
        lifecycle.Publish(new NodeDisposed(handle.Name, ParentDisplayName(handle.ParentId)));
    }

    private string ParentDisplayName(NodeId? parentId)
    {
        if (parentId is null)
        {
            return string.Empty;
        }

        if (parentId.Value.IsUser)
        {
            return NodeId.User.Value;
        }

        return TryGet(parentId.Value, out var parent) ? parent.Name : parentId.Value.Value;
    }
}
