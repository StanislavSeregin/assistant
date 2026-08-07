using Assistant.App.Mail;
using Assistant.App.Runtime;
using System;
using System.Collections.Generic;

namespace Assistant.App.UI.Abstractions;

public sealed record ChildNodeInfo(string Name, string Description);

/// <summary>
/// User-node operations for the interactive workspace (same mail/org ops as a parent agent).
/// </summary>
public interface IUserWorkspace
{
    event EventHandler? InboxChanged;

    event EventHandler? ChildrenChanged;

    /// <summary>
    /// Fired when model activity may have changed (turn boundaries, support enter/exit).
    /// Busy detail: <see cref="GetAgentActivity"/>.
    /// </summary>
    event EventHandler? AgentActivityChanged;

    string DefaultMailSubject { get; }

    /// <summary>Configured templates whose Name is not already a direct child of User.</summary>
    IReadOnlyList<AgentTemplate> ListAvailableAgentTemplates();

    IReadOnlyList<InboxItem> ListInbox();

    MailMessage? FindMail(string mailId);

    /// <summary>Marks read and returns the message, or null if missing.</summary>
    MailMessage? ReadMail(string mailId);

    (bool Ok, string Message) ReplyMail(string mailId, string body);

    (bool Ok, string Message) DeleteMail(string mailId);

    (bool Ok, string Message) WriteMail(string toName, string subject, string body);

    IReadOnlyList<ChildNodeInfo> ListChildren();

    /// <summary>Any graph turn or sideband model work is in flight.</summary>
    bool AnyAgentBusy { get; }

    /// <summary>Busy snapshot for a User-direct child row (spinner + optional actor).</summary>
    AgentRowActivity GetAgentActivity(string name);

    (bool Ok, string Message) SpawnChild(AgentTemplate template);

    (bool Ok, string Message) SpawnChild(
        string name,
        string description,
        string instructions);

    (bool Ok, string Message) DisposeChild(string name);

    /// <summary>Called from the lifecycle UI handler so lists can refresh reactively.</summary>
    void NotifyLifecycle(Lifecycle.ILifecycleEvent lifecycleEvent);
}
