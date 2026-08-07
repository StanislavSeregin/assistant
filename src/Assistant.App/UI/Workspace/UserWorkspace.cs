using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Tools;
using Assistant.App.UI.Abstractions;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Assistant.App.UI.Workspace;

public sealed class UserWorkspace : IUserWorkspace
{
    private readonly NodeRegistry _registry;
    private readonly MailService _mail;
    private readonly AgentBootstrap _bootstrap;
    private readonly SessionCheckpoint _checkpoint;
    private readonly ModelActivityTracker _activity;
    private readonly IOptions<Settings> _settings;

    public UserWorkspace(
        NodeRegistry registry,
        MailService mail,
        AgentBootstrap bootstrap,
        SessionCheckpoint checkpoint,
        ModelActivityTracker activity,
        IOptions<Settings> settings)
    {
        _registry = registry;
        _mail = mail;
        _bootstrap = bootstrap;
        _checkpoint = checkpoint;
        _activity = activity;
        _settings = settings;
        _activity.Changed += OnActivityChanged;
    }

    public event EventHandler? InboxChanged;

    public event EventHandler? ChildrenChanged;

    public event EventHandler? AgentActivityChanged;

    public string DefaultMailSubject => _settings.Value.UserMailSubject;

    public bool AnyAgentBusy => _activity.AnyBusy;

    public IReadOnlyList<AgentTemplate> ListAvailableAgentTemplates()
    {
        var inUse = ListChildren()
            .Select(c => c.Name)
            .ToHashSet(StringComparer.Ordinal);

        return _settings.Value.AgentTemplates
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

        var wasNew = before.Status == MailStatus.New;
        _mail.ReadMail(user, mailId);
        if (wasNew)
        {
            InboxChanged?.Invoke(this, EventArgs.Empty);
        }

        return user.Inbox.Find(mailId);
    }

    public (bool Ok, string Message) ReplyMail(string mailId, string body) =>
        _mail.ReplyMail(RequireUser(), mailId, body);

    public (bool Ok, string Message) DeleteMail(string mailId) =>
        _mail.DeleteMail(RequireUser(), mailId);

    public (bool Ok, string Message) WriteMail(string toName, string subject, string body) =>
        _mail.WriteMail(RequireUser(), toName, subject, body);

    public IReadOnlyList<ChildNodeInfo> ListChildren()
    {
        var user = RequireUser();
        return _registry.ListChildren(user)
            .Select(c => new ChildNodeInfo(c.Name, c.Description))
            .ToArray();
    }

    public AgentRowActivity GetAgentActivity(string name) =>
        _activity.ForUserChild(name);

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
            var child = _registry.SpawnChild(
                user,
                name,
                description,
                instructions,
                parentRole: _settings.Value.UserDescription);
            _bootstrap.Bootstrap(child);
            _checkpoint.SaveEmptySession(child);
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
            var disposed = _registry.DisposeSubtree(user, name);
            var names = disposed.Select(n => n.Name).ToArray();
            foreach (var handle in disposed)
            {
                handle.Inbox.Clear();
            }

            // Purge publishes MailPurged on the lifecycle bus (async). Raise InboxChanged
            // here too so the UI does not depend on the bus for a local dispose.
            _mail.PurgeMailFrom(user, names);
            InboxChanged?.Invoke(this, EventArgs.Empty);
            _activity.Pulse();
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
                ChildrenChanged?.Invoke(this, EventArgs.Empty);
                break;
            case NodeDisposed disposed:
                if (disposed.Parent == "User")
                {
                    ChildrenChanged?.Invoke(this, EventArgs.Empty);
                }

                _activity.Pulse();
                break;
            case TurnStarted:
            case TurnEnded:
                _activity.Pulse();
                break;
        }
    }

    private void OnActivityChanged(object? sender, EventArgs e) =>
        AgentActivityChanged?.Invoke(this, EventArgs.Empty);

    private NodeHandle RequireUser() =>
        _registry.User
        ?? throw new InvalidOperationException("User node is not registered.");
}
