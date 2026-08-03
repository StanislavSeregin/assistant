using Assistant.App.Registry;
using LiteDB;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Assistant.App.Persistence;

public sealed class LiteDbAgentStateStore : IAgentStateStore, IDisposable
{
    private const string NodesCollection = "nodes";
    private const string MailCollection = "mail";
    private const string SessionsCollection = "sessions";
    private const string MetaCollection = "meta";

    private readonly LiteDatabase _db;

    public LiteDbAgentStateStore(IOptions<Settings> settings)
    {
        var path = ResolveDbPath(settings.Value.StateDbPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Single shared LiteDatabase instance: LiteDB v5 handles concurrent readers
        // and serializes writers per collection — no extra process locks needed.
        _db = new LiteDatabase(path);
        Mail().EnsureIndex(m => m.OwnerNodeId);
    }

    public bool HasPersistedNodes() => Nodes().Count() > 0;

    public IReadOnlyList<PersistedNodeDocument> LoadAllNodes() => Nodes().FindAll().ToList();

    public IReadOnlyList<PersistedMailDocument> LoadAllMail() => Mail().FindAll().ToList();

    public PersistedSessionDocument? LoadSession(string nodeId) => Sessions().FindById(nodeId);

    public long LoadMailSeq() =>
        Meta().FindById(PersistedMetaDocument.SingletonId)?.MailSeq ?? 0;

    public void SaveNode(NodeHandle node, TurnActivitySnapshot? turn = null)
    {
        if (node.IsDisposed)
        {
            return;
        }

        // Read-modify-write under a write transaction so concurrent identity upserts
        // cannot clobber turn flags mid-flight.
        InWriteTransaction(() =>
        {
            if (node.IsDisposed)
            {
                return;
            }

            var nodes = Nodes();
            var existing = nodes.FindById(node.Id.Value);
            // Checkpoint after DeleteNodes must not resurrect a removed agent.
            if (existing is null && turn is not null)
            {
                return;
            }

            nodes.Upsert(PersistedMappings.FromNode(node, turn, existing));
        });
    }

    public void SaveInbox(NodeHandle node)
    {
        if (node.IsDisposed)
        {
            return;
        }

        var ownerId = node.Id.Value;
        InWriteTransaction(() =>
        {
            if (node.IsDisposed || Nodes().FindById(ownerId) is null)
            {
                return;
            }

            var mail = Mail();
            mail.DeleteMany(m => m.OwnerNodeId == ownerId);
            foreach (var message in node.Inbox.Snapshot())
            {
                mail.Insert(PersistedMappings.FromMail(ownerId, message));
            }
        });
    }

    public void SaveSession(NodeHandle node, IReadOnlyList<ChatMessage> messages, JsonElement stateBag)
    {
        if (node.IsDisposed)
        {
            return;
        }

        InWriteTransaction(() =>
        {
            if (node.IsDisposed || Nodes().FindById(node.Id.Value) is null)
            {
                return;
            }

            Sessions().Upsert(PersistedMappings.FromSession(node.Id.Value, messages, stateBag));
        });
    }

    public void SaveMailSeq(long mailSeq) =>
        Meta().Upsert(new PersistedMetaDocument
        {
            Id = PersistedMetaDocument.SingletonId,
            MailSeq = mailSeq,
            SchemaVersion = 1
        });

    public void DeleteNodes(IEnumerable<string> nodeIds)
    {
        var ids = nodeIds.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0)
        {
            return;
        }

        InWriteTransaction(() => DeleteNodesCore(ids));
    }

    public void CommitSubtreeDisposal(NodeHandle parent, IReadOnlyList<string> deletedNodeIds)
    {
        var ids = deletedNodeIds.Distinct(StringComparer.Ordinal).ToArray();
        InWriteTransaction(() =>
        {
            var nodes = Nodes();
            var existing = nodes.FindById(parent.Id.Value);
            nodes.Upsert(PersistedMappings.FromNode(parent, turn: null, existing));
            if (ids.Length > 0)
            {
                DeleteNodesCore(ids);
            }
        });
    }

    public void Dispose() => _db.Dispose();

    private void DeleteNodesCore(IReadOnlyList<string> ids)
    {
        var nodes = Nodes();
        var mail = Mail();
        var sessions = Sessions();
        foreach (var id in ids)
        {
            nodes.Delete(id);
            sessions.Delete(id);
            mail.DeleteMany(m => m.OwnerNodeId == id);
        }
    }

    private void InWriteTransaction(Action action)
    {
        _db.BeginTrans();
        try
        {
            action();
            _db.Commit();
        }
        catch
        {
            _db.Rollback();
            throw;
        }
    }

    private ILiteCollection<PersistedNodeDocument> Nodes() =>
        _db.GetCollection<PersistedNodeDocument>(NodesCollection);

    private ILiteCollection<PersistedMailDocument> Mail() =>
        _db.GetCollection<PersistedMailDocument>(MailCollection);

    private ILiteCollection<PersistedSessionDocument> Sessions() =>
        _db.GetCollection<PersistedSessionDocument>(SessionsCollection);

    private ILiteCollection<PersistedMetaDocument> Meta() =>
        _db.GetCollection<PersistedMetaDocument>(MetaCollection);

    private static string ResolveDbPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Assistant");
        return Path.Combine(root, "state.litedb");
    }
}
