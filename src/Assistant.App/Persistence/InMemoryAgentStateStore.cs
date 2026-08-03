using Assistant.App.Mail;
using Assistant.App.Registry;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Assistant.App.Persistence;

/// <summary>In-memory store for smoke tests (no LiteDB file).</summary>
public sealed class InMemoryAgentStateStore : IAgentStateStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PersistedNodeDocument> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PersistedMailDocument> _mail = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PersistedSessionDocument> _sessions = new(StringComparer.Ordinal);
    private long _mailSeq;

    public bool HasPersistedNodes()
    {
        lock (_gate)
        {
            return _nodes.Count > 0;
        }
    }

    public IReadOnlyList<PersistedNodeDocument> LoadAllNodes()
    {
        lock (_gate)
        {
            return _nodes.Values.ToList();
        }
    }

    public IReadOnlyList<PersistedMailDocument> LoadAllMail()
    {
        lock (_gate)
        {
            return _mail.Values.ToList();
        }
    }

    public PersistedSessionDocument? LoadSession(string nodeId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(nodeId, out var session) ? session : null;
        }
    }

    public long LoadMailSeq()
    {
        lock (_gate)
        {
            return _mailSeq;
        }
    }

    public void SaveNode(NodeHandle node, TurnActivitySnapshot? turn = null)
    {
        if (node.IsDisposed)
        {
            return;
        }

        lock (_gate)
        {
            if (node.IsDisposed)
            {
                return;
            }

            _nodes.TryGetValue(node.Id.Value, out var existing);
            if (existing is null && turn is not null)
            {
                return;
            }

            _nodes[node.Id.Value] = PersistedMappings.FromNode(node, turn, existing);
        }
    }

    public void SaveInbox(NodeHandle node)
    {
        if (node.IsDisposed)
        {
            return;
        }

        lock (_gate)
        {
            if (node.IsDisposed || !_nodes.ContainsKey(node.Id.Value))
            {
                return;
            }

            var ownerId = node.Id.Value;
            foreach (var key in _mail.Keys.Where(k => PersistedMappings.IsMailOwnedBy(k, ownerId)).ToArray())
            {
                _mail.Remove(key);
            }

            foreach (var message in node.Inbox.Snapshot())
            {
                var doc = PersistedMappings.FromMail(ownerId, message);
                _mail[doc.Id] = doc;
            }
        }
    }

    public void SaveSession(NodeHandle node, IReadOnlyList<ChatMessage> messages, JsonElement stateBag)
    {
        if (node.IsDisposed)
        {
            return;
        }

        lock (_gate)
        {
            if (node.IsDisposed || !_nodes.ContainsKey(node.Id.Value))
            {
                return;
            }

            _sessions[node.Id.Value] = PersistedMappings.FromSession(node.Id.Value, messages, stateBag);
        }
    }

    public void SaveMailSeq(long mailSeq)
    {
        lock (_gate)
        {
            _mailSeq = mailSeq;
        }
    }

    public void DeleteNodes(IEnumerable<string> nodeIds)
    {
        lock (_gate)
        {
            DeleteNodesCore(nodeIds);
        }
    }

    public void CommitSubtreeDisposal(NodeHandle parent, IReadOnlyList<string> deletedNodeIds)
    {
        lock (_gate)
        {
            _nodes.TryGetValue(parent.Id.Value, out var existing);
            _nodes[parent.Id.Value] = PersistedMappings.FromNode(parent, turn: null, existing);
            DeleteNodesCore(deletedNodeIds);
        }
    }

    private void DeleteNodesCore(IEnumerable<string> nodeIds)
    {
        foreach (var id in nodeIds.Distinct(StringComparer.Ordinal))
        {
            _nodes.Remove(id);
            _sessions.Remove(id);
            foreach (var key in _mail.Keys.Where(k => PersistedMappings.IsMailOwnedBy(k, id)).ToArray())
            {
                _mail.Remove(key);
            }
        }
    }
}
