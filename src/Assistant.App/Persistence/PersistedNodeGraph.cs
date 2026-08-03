using Assistant.App.Registry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistant.App.Persistence;

/// <summary>
/// Reachability over persisted <see cref="PersistedNodeDocument.ChildrenByName"/> edges.
/// </summary>
internal static class PersistedNodeGraph
{
    /// <summary>
    /// Node ids reachable from User by walking ChildrenByName. Empty if User is missing.
    /// </summary>
    public static HashSet<string> ReachableFromUser(IReadOnlyList<PersistedNodeDocument> nodes)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        if (!byId.TryGetValue(NodeId.User.Value, out var user))
        {
            return reachable;
        }

        reachable.Add(user.Id);
        var queue = new Queue<string>();
        foreach (var childId in user.ChildrenByName.Values)
        {
            queue.Enqueue(childId);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reachable.Add(id) || !byId.TryGetValue(id, out var doc))
            {
                continue;
            }

            foreach (var childId in doc.ChildrenByName.Values)
            {
                queue.Enqueue(childId);
            }
        }

        return reachable;
    }

    /// <summary>
    /// Deletes node/session/mail rows not reachable from User, then returns the survivors.
    /// Heals the dispose/checkpoint race that left orphans waking on next launch.
    /// </summary>
    public static IReadOnlyList<PersistedNodeDocument> PruneOrphans(
        IAgentStateStore store,
        IReadOnlyList<PersistedNodeDocument> nodes)
    {
        var reachable = ReachableFromUser(nodes);
        if (reachable.Count == 0)
        {
            return nodes;
        }

        var orphanIds = nodes
            .Where(n => !reachable.Contains(n.Id))
            .Select(n => n.Id)
            .ToArray();
        if (orphanIds.Length > 0)
        {
            store.DeleteNodes(orphanIds);
        }

        return nodes.Where(n => reachable.Contains(n.Id)).ToArray();
    }
}
