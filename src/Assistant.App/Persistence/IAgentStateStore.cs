using Assistant.App.Registry;
using Microsoft.Extensions.AI;
using System.Collections.Generic;
using System.Text.Json;

namespace Assistant.App.Persistence;

public interface IAgentStateStore
{
    bool HasPersistedNodes();

    IReadOnlyList<PersistedNodeDocument> LoadAllNodes();

    IReadOnlyList<PersistedMailDocument> LoadAllMail();

    PersistedSessionDocument? LoadSession(string nodeId);

    long LoadMailSeq();

    void SaveNode(NodeHandle node, TurnActivitySnapshot? turn = null);

    void SaveInbox(NodeHandle node);

    void SaveSession(NodeHandle node, IReadOnlyList<ChatMessage> messages, JsonElement stateBag);

    void SaveMailSeq(long mailSeq);

    void DeleteNodes(IEnumerable<string> nodeIds);

    /// <summary>
    /// Atomically persist the parent (without disposed children) and delete the subtree
    /// rows (node + session + owned mail). Prevents orphans from a crash between
    /// separate SaveNode/DeleteNodes calls.
    /// </summary>
    void CommitSubtreeDisposal(NodeHandle parent, IReadOnlyList<string> deletedNodeIds);
}

/// <summary>Turn flags persisted alongside the node graph.</summary>
public readonly record struct TurnActivitySnapshot(
    bool DidHandleMail,
    bool DidCommitContext,
    bool TurnInProgress);
