using Assistant.App.Checklist;
using Assistant.App.Mail;
using Assistant.App.Registry;
using System;
using System.Collections.Generic;

namespace Assistant.App.Persistence;

public sealed class PersistedNodeDocument
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Instructions { get; set; } = string.Empty;

    public string? ParentId { get; set; }

    public string? ParentDescription { get; set; }

    public NodeRuntimeKind RuntimeKind { get; set; }

    public Dictionary<string, string> ChildrenByName { get; set; } = new(StringComparer.Ordinal);

    public string? ContinuityHandoff { get; set; }

    /// <summary>Open checklist items; missing/null on old DBs means empty.</summary>
    public List<PersistedChecklistItem>? Checklist { get; set; }

    public bool DidHandleMail { get; set; }

    public bool DidMutateChecklist { get; set; }

    public bool DidCommitContext { get; set; }

    /// <summary>True when a turn was in progress (non-empty history, not yet committed).</summary>
    public bool TurnInProgress { get; set; }
}

public sealed class PersistedMailDocument
{
    public string Id { get; set; } = string.Empty;

    public string OwnerNodeId { get; set; } = string.Empty;

    public string MailId { get; set; } = string.Empty;

    public DateTime Timestamp { get; set; }

    public string From { get; set; } = string.Empty;

    public string To { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsFromParent { get; set; }

    public Guid ThreadId { get; set; }

    public MailStatus Status { get; set; }
}

public sealed class PersistedSessionDocument
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Full turn history as a JSON array of ChatMessage (whole-array overwrite).</summary>
    public string MessagesJson { get; set; } = "[]";

    /// <summary>Serialized AgentSession.StateBag (todos and other provider state).</summary>
    public string StateBagJson { get; set; } = "{}";
}

public sealed class PersistedMetaDocument
{
    public const string SingletonId = "meta";

    public string Id { get; set; } = SingletonId;

    public long MailSeq { get; set; }

    public int SchemaVersion { get; set; } = 1;
}
