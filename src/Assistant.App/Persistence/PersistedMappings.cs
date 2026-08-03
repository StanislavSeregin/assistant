using Assistant.App.Mail;
using Assistant.App.Registry;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Assistant.App.Persistence;

internal static class PersistedMappings
{
    public static PersistedNodeDocument FromNode(
        NodeHandle node,
        TurnActivitySnapshot? turn,
        PersistedNodeDocument? existing)
    {
        var doc = new PersistedNodeDocument
        {
            Id = node.Id.Value,
            Name = node.Name,
            Description = node.Description,
            Instructions = node.Instructions,
            ParentId = node.ParentId?.Value,
            ParentDescription = node.ParentDescription,
            RuntimeKind = node.RuntimeKind,
            ChildrenByName = node.ChildrenByName.ToDictionary(
                static p => p.Key,
                static p => p.Value.Value,
                StringComparer.Ordinal),
            ContinuityHandoff = node.Llm?.ContinuityHandoff
        };

        if (turn is { } snapshot)
        {
            doc.DidHandleMail = snapshot.DidHandleMail;
            doc.DidCommitContext = snapshot.DidCommitContext;
            doc.TurnInProgress = snapshot.TurnInProgress;
        }
        else if (existing is not null)
        {
            // Identity-only upsert must not wipe mid-turn flags (e.g. SpawnChild of a peer).
            doc.DidHandleMail = existing.DidHandleMail;
            doc.DidCommitContext = existing.DidCommitContext;
            doc.TurnInProgress = existing.TurnInProgress;
        }

        return doc;
    }

    public static PersistedMailDocument FromMail(string ownerNodeId, MailMessage message) =>
        new()
        {
            Id = MailDocumentId(ownerNodeId, message.Id),
            OwnerNodeId = ownerNodeId,
            MailId = message.Id,
            Timestamp = message.Timestamp,
            From = message.From,
            To = message.To,
            Subject = message.Subject,
            Body = message.Body,
            IsFromParent = message.IsFromParent,
            ThreadId = message.ThreadId,
            Status = message.Status
        };

    public static MailMessage ToMailMessage(PersistedMailDocument doc) =>
        new()
        {
            Id = doc.MailId,
            Timestamp = doc.Timestamp,
            From = doc.From,
            To = doc.To,
            Subject = doc.Subject,
            Body = doc.Body,
            IsFromParent = doc.IsFromParent,
            ThreadId = doc.ThreadId,
            Status = doc.Status
        };

    public static PersistedSessionDocument FromSession(
        string nodeId,
        IReadOnlyList<ChatMessage> messages,
        JsonElement stateBag) =>
        new()
        {
            Id = nodeId,
            MessagesJson = JsonSerializer.Serialize(messages, AIJsonUtilities.DefaultOptions),
            StateBagJson = stateBag.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "{}"
                : stateBag.GetRawText()
        };

    public static string MailDocumentId(string ownerNodeId, string mailId) =>
        $"{ownerNodeId}:{mailId}";

    public static bool IsMailOwnedBy(string mailDocumentId, string ownerNodeId) =>
        mailDocumentId.StartsWith(ownerNodeId + ":", StringComparison.Ordinal);
}
