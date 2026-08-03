using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using Assistant.App.Tools;
using Assistant.App.UI.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistant.App.UI.Workspace;

public sealed class UserWorkspace(
    NodeRegistry registry,
    MailService mail,
    AgentBootstrap bootstrap,
    SessionCheckpoint checkpoint,
    IOptions<Settings> settings) : IUserWorkspace
{
    public event EventHandler? InboxChanged;

    public event EventHandler? ChildrenChanged;

    public string DefaultMailSubject => settings.Value.UserMailSubject;

    public IReadOnlyList<AgentTemplate> ListAvailableAgentTemplates()
    {
        var inUse = ListChildren()
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        return settings.Value.AgentTemplates
            .Where(t => !string.IsNullOrWhiteSpace(t.Name) && !inUse.Contains(t.Name))
            .ToArray();
    }

    public IReadOnlyList<InboxItem> ListInbox() =>
        RequireUser().Inbox.List();

    public MailMessage? FindMail(string mailId) =>
        RequireUser().Inbox.Find(mailId);

    public MailMessage? ReadMail(string mailId)
    {
        var user = RequireUser();
        var before = user.Inbox.Find(mailId);
        if (before is null)
        {
            return null;
        }

        mail.ReadMail(user, mailId);
        return user.Inbox.Find(mailId);
    }

    public (bool Ok, string Message) ReplyMail(string mailId, string body) =>
        mail.ReplyMail(RequireUser(), mailId, body);

    public (bool Ok, string Message) DeleteMail(string mailId) =>
        mail.DeleteMail(RequireUser(), mailId);

    public (bool Ok, string Message) WriteMail(string toName, string subject, string body) =>
        mail.WriteMail(RequireUser(), toName, subject, body);

    public IReadOnlyList<ChildNodeInfo> ListChildren()
    {
        var user = RequireUser();
        return registry.ListChildren(user)
            .Select(c => new ChildNodeInfo(c.Name, c.Description))
            .ToArray();
    }

    public (bool Ok, string Message) SpawnChild(AgentTemplate template) =>
        SpawnChild(template.Name, template.Description, template.Instructions);

    public (bool Ok, string Message) SpawnChild(
        string name,
        string description,
        string instructions)
    {
        try
        {
            var user = RequireUser();
            var child = registry.SpawnChild(
                user,
                name,
                description,
                instructions,
                parentRole: settings.Value.UserDescription);
            bootstrap.Bootstrap(child);
            checkpoint.SaveEmptySession(child);
            return (true, $"Spawned '{child.Name}'.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public (bool Ok, string Message) DisposeChild(string name)
    {
        try
        {
            var user = RequireUser();
            var disposed = registry.DisposeSubtree(user, name);
            var names = disposed.Select(n => n.Name).ToArray();
            foreach (var handle in disposed)
            {
                handle.Inbox.Clear();
            }

            // Purge publishes MailPurged on the lifecycle bus (async). Raise InboxChanged
            // here too so the UI does not depend on the bus for a local dispose.
            mail.PurgeMailFrom(user, names);
            InboxChanged?.Invoke(this, EventArgs.Empty);
            return (true, $"Disposed '{name}' and subtree ({names.Length} node(s)).");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public void NotifyLifecycle(ILifecycleEvent lifecycleEvent)
    {
        switch (lifecycleEvent)
        {
            case MailReceived { Node: "User" }:
            case MailReplied { Node: "User" }:
            case MailDeleted { Node: "User" }:
            case MailPurged { Node: "User" }:
                InboxChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NodeSpawned { Parent: "User" }:
            case NodeDisposed { Parent: "User" }:
                ChildrenChanged?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private NodeHandle RequireUser() =>
        registry.User
        ?? throw new InvalidOperationException("User node is not registered.");
}
