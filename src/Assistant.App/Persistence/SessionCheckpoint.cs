using Assistant.App.Registry;
using Assistant.App.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Persistence;

/// <summary>Snapshots in-memory session state into <see cref="IAgentStateStore"/>.</summary>
public sealed class SessionCheckpoint(IAgentStateStore store)
{
    public void CheckpointSession(NodeHandle node, TurnActivity? activity = null)
    {
        if (node.IsDisposed || node.Llm?.Session is null)
        {
            return;
        }

        var session = node.Llm.Session;
        IReadOnlyList<ChatMessage> messages = session.TryGetInMemoryChatHistory(out var history)
            ? history
            : [];

        store.SaveSession(node, messages, session.StateBag.Serialize());
        store.SaveNode(node, ToTurnSnapshot(activity, messages.Count));
    }

    /// <summary>After CommitContext: empty history, handoff already on <see cref="LlmNodeRuntime"/>.</summary>
    public void CheckpointClearedSession(NodeHandle node)
    {
        if (node.IsDisposed || node.Llm?.Session is null)
        {
            return;
        }

        store.SaveSession(node, Array.Empty<ChatMessage>(), node.Llm.Session.StateBag.Serialize());
        store.SaveNode(
            node,
            new TurnActivitySnapshot(
                DidHandleMail: true,
                DidCommitContext: true,
                TurnInProgress: false));
    }

    /// <summary>Persist an empty session right after bootstrap/spawn.</summary>
    public void SaveEmptySession(NodeHandle node)
    {
        if (node.IsDisposed || node.Llm?.Session is null)
        {
            return;
        }

        store.SaveSession(node, Array.Empty<ChatMessage>(), node.Llm.Session.StateBag.Serialize());
    }

    public static async Task ApplyPersistedSessionAsync(
        NodeHandle handle,
        PersistedSessionDocument persisted,
        CancellationToken cancellationToken = default)
    {
        if (handle.Llm?.Agent is null)
        {
            return;
        }

        var agent = handle.Llm.Agent;
        var messages = DeserializeMessages(persisted.MessagesJson);
        AgentSession session;

        if (!string.IsNullOrWhiteSpace(persisted.StateBagJson)
            && persisted.StateBagJson is not "{}")
        {
            using var stateDoc = JsonDocument.Parse(persisted.StateBagJson);
            var payload = BuildSessionPayload(stateDoc.RootElement);
            session = await agent.DeserializeSessionAsync(payload, cancellationToken: cancellationToken);
        }
        else
        {
            session = handle.Llm.Session
                      ?? await agent.CreateSessionAsync(cancellationToken);
        }

        session.SetInMemoryChatHistory(messages);
        handle.Llm.BindSession(agent, session);
        handle.Llm.NeedsResumeTurn = messages.Count > 0 && !handle.Llm.PersistedDidCommitContext;
    }

    public static List<ChatMessage> DeserializeMessages(string messagesJson)
    {
        if (string.IsNullOrWhiteSpace(messagesJson) || messagesJson is "[]")
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ChatMessage>>(messagesJson, AIJsonUtilities.DefaultOptions)
               ?? [];
    }

    private static TurnActivitySnapshot ToTurnSnapshot(TurnActivity? activity, int messageCount)
    {
        if (activity is null)
        {
            return new TurnActivitySnapshot(
                DidHandleMail: false,
                DidCommitContext: false,
                TurnInProgress: messageCount > 0);
        }

        return new TurnActivitySnapshot(
            activity.DidHandleMail,
            activity.DidCommitContext,
            TurnInProgress: messageCount > 0 && !activity.DidCommitContext);
    }

    private static JsonElement BuildSessionPayload(JsonElement stateBag)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WritePropertyName("stateBag");
            stateBag.WriteTo(writer);
            writer.WriteEndObject();
        }

        using var doc = JsonDocument.Parse(buffer.WrittenMemory);
        return doc.RootElement.Clone();
    }
}
