using Assistant.App.Lifecycle;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace Assistant.App.Registry;

public sealed class AgentRegistry(ILifecycleSink lifecycle)
{
    private readonly ConcurrentDictionary<AgentId, AgentHandle> _agents = new();
    private readonly Channel<AgentHandle> _registered = Channel.CreateUnbounded<AgentHandle>(
        new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false
        });
    private AgentId? _rootId;

    public AgentId? RootId => _rootId;

    public AgentHandle? Root =>
        _rootId is { } id && _agents.TryGetValue(id, out var handle) ? handle : null;

    public ChannelReader<AgentHandle> Registered => _registered.Reader;

    public bool TryGet(AgentId id, out AgentHandle handle) => _agents.TryGetValue(id, out handle!);

    public bool TryGetByName(AgentId parentId, string name, out AgentHandle handle)
    {
        handle = null!;
        if (!_agents.TryGetValue(parentId, out var parent))
        {
            return false;
        }

        if (!parent.ChildrenByName.TryGetValue(name, out var childId))
        {
            return false;
        }

        return _agents.TryGetValue(childId, out handle!);
    }

    public AgentHandle RegisterRoot(
        string name,
        string description,
        string instructions,
        string userDescription)
    {
        if (_rootId is not null)
        {
            throw new InvalidOperationException("Root agent is already registered.");
        }

        var id = new AgentId(name);
        var handle = new AgentHandle(
            id,
            name,
            description,
            instructions,
            AgentId.User,
            userDescription);

        if (!_agents.TryAdd(id, handle))
        {
            throw new InvalidOperationException($"Agent '{name}' already exists.");
        }

        _rootId = id;
        lifecycle.Publish(new AgentSpawned(
            name,
            id.Value,
            AgentId.User.Value,
            description,
            instructions,
            userDescription));
        _registered.Writer.TryWrite(handle);
        return handle;
    }

    public AgentHandle SpawnChild(
        AgentHandle parent,
        string name,
        string description,
        string instructions,
        string parentRole)
    {
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Name is empty.", nameof(name));
        }

        if (string.Equals(name, AgentId.User.Value, StringComparison.Ordinal)
            || string.Equals(name, parent.Name, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{name}' is reserved.");
        }

        if (parent.ChildrenByName.ContainsKey(name))
        {
            throw new InvalidOperationException($"Subagent '{name}' already exists.");
        }

        var id = new AgentId($"{parent.Name}/{name}");
        var handle = new AgentHandle(
            id,
            name,
            description.Trim(),
            instructions.Trim(),
            parent.Id,
            string.IsNullOrWhiteSpace(parentRole) ? parent.Description : parentRole.Trim());

        if (!_agents.TryAdd(id, handle))
        {
            throw new InvalidOperationException($"Agent id '{id}' already exists.");
        }

        if (!parent.ChildrenByName.TryAdd(name, id))
        {
            _agents.TryRemove(id, out _);
            throw new InvalidOperationException($"Subagent '{name}' already exists.");
        }

        lifecycle.Publish(new AgentSpawned(
            name,
            id.Value,
            parent.Name,
            handle.Description,
            handle.Instructions,
            handle.ParentDescription));
        _registered.Writer.TryWrite(handle);
        return handle;
    }

    public IReadOnlyList<AgentHandle> DisposeSubtree(AgentHandle parent, string childName)
    {
        childName = childName.Trim();
        if (!parent.ChildrenByName.TryRemove(childName, out var childId)
            || !_agents.TryGetValue(childId, out var child))
        {
            throw new InvalidOperationException($"Subagent '{childName}' not found.");
        }

        var disposed = new List<AgentHandle>();
        DisposeRecursive(child, disposed);
        return disposed;
    }

    public IEnumerable<AgentHandle> All() => _agents.Values;

    private void DisposeRecursive(AgentHandle handle, List<AgentHandle> disposed)
    {
        foreach (var childName in handle.ChildrenByName.Keys.ToArray())
        {
            if (handle.ChildrenByName.TryRemove(childName, out var childId)
                && _agents.TryGetValue(childId, out var child))
            {
                DisposeRecursive(child, disposed);
            }
        }

        handle.MarkDisposed();
        _agents.TryRemove(handle.Id, out _);
        disposed.Add(handle);
        lifecycle.Publish(new AgentDisposed(handle.Name, ParentDisplayName(handle.ParentId)));
    }

    private string ParentDisplayName(AgentId parentId)
    {
        if (parentId.IsUser)
        {
            return AgentId.User.Value;
        }

        return TryGet(parentId, out var parent) ? parent.Name : parentId.Value;
    }
}
