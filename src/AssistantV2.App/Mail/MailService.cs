using AssistantV2.App.Lifecycle;
using AssistantV2.App.Registry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace AssistantV2.App.Mail;

public sealed class MailService(
    AgentRegistry registry,
    ILifecycleSink lifecycle)
{
    private long _mailSeq;

    public IReadOnlyList<(string Name, string Role, bool IsParent)> GetRecipients(AgentHandle agent)
    {
        var list = new List<(string, string, bool)>();

        if (agent.ParentId.IsUser)
        {
            list.Add((
                AgentId.User.Value,
                agent.ParentDescription ?? "User",
                true));
        }
        else if (registry.TryGet(agent.ParentId, out var parent))
        {
            list.Add((parent.Name, parent.Description, true));
        }

        foreach (var (name, childId) in agent.ChildrenByName.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (registry.TryGet(childId, out var child))
            {
                list.Add((child.Name, child.Description, false));
            }
            else
            {
                list.Add((name, string.Empty, false));
            }
        }

        return list;
    }

    public IReadOnlyList<InboxItem> ListInbox(AgentHandle agent) => agent.Inbox.List();

    public string ReadMail(AgentHandle agent, string mailId)
    {
        var message = agent.Inbox.FindAndMarkRead(mailId);
        if (message is null)
        {
            return $"Mail '{mailId}' not found.";
        }

        lifecycle.Publish(new MailRead(
            agent.Name,
            message.Id,
            message.From,
            message.Subject,
            message.Body,
            message.Timestamp));

        return $"""
            id: {message.Id}
            time: {MailTimestamp.FormatUtc(message.Timestamp)}
            from: {message.From}
            subject: {message.Subject}
            ---
            {message.Body}
            """;
    }

    public (bool Ok, string Message) WriteMail(AgentHandle from, string toName, string subject, string body)
    {
        toName = toName.Trim();
        subject = subject.Trim();
        body = body.Trim();

        if (string.IsNullOrWhiteSpace(toName))
        {
            return (false, "Recipient is empty.");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return (false, "Subject is empty.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, "Body is empty.");
        }

        if (!TryResolveRecipient(from, toName, out var toId, out var error))
        {
            return (false, error);
        }

        var mail = CreateMail(
            from.Name,
            ResolveDisplayName(toId, toName),
            subject,
            body,
            isFromParent: IsParentOf(from.Id, toId),
            threadId: Guid.NewGuid());

        return (true, Deliver(from, toId, toName, mail, isReply: false));
    }

    public (bool Ok, string Message) ReplyMail(AgentHandle from, string mailId, string body)
    {
        body = body.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return (false, "Body is empty.");
        }

        var original = from.Inbox.Find(mailId);
        if (original is null)
        {
            return (false, $"Mail '{mailId}' not found.");
        }

        if (!TryResolveRecipient(from, original.From, out var toId, out var error))
        {
            return (false, error);
        }

        if (!from.Inbox.TryRemove(mailId, out _))
        {
            return (false, $"Mail '{mailId}' not found.");
        }

        lifecycle.Publish(new MailReplied(from.Name, mailId));

        var subject = original.Subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase)
            ? original.Subject
            : $"Re: {original.Subject}";

        var mail = CreateMail(
            from.Name,
            ResolveDisplayName(toId, original.From),
            subject,
            body,
            isFromParent: IsParentOf(from.Id, toId),
            threadId: original.ThreadId);

        return (true, Deliver(from, toId, original.From, mail, isReply: true));
    }

    public (bool Ok, string Message) DeleteMail(AgentHandle agent, string mailId)
    {
        if (!agent.Inbox.TryRemove(mailId, out _))
        {
            return (false, $"Mail '{mailId}' not found.");
        }

        lifecycle.Publish(new MailDeleted(agent.Name, mailId));
        return (true, $"Deleted mail '{mailId}' from inbox.");
    }

    public string WriteFromUser(string body, string subject)
    {
        var root = registry.Root
            ?? throw new InvalidOperationException("Root agent is not registered.");

        body = body.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Body is empty.";
        }

        var mail = CreateMail(
            AgentId.User.Value,
            root.Name,
            subject.Trim(),
            body,
            isFromParent: true,
            threadId: Guid.NewGuid());

        DeliverToAgent(root, mail);
        lifecycle.Publish(new MailSent(
            AgentId.User.Value,
            root.Name,
            mail.Id,
            mail.Subject,
            mail.Body,
            IsReply: false));
        return mail.Id;
    }

    public int PurgeMailFrom(AgentHandle agent, IEnumerable<string> fromNames)
    {
        var total = 0;
        foreach (var name in fromNames.Distinct(StringComparer.Ordinal))
        {
            var count = agent.Inbox.PurgeFrom(name);
            if (count > 0)
            {
                total += count;
                lifecycle.Publish(new MailPurged(agent.Name, name, count));
            }
        }

        return total;
    }

    private string Deliver(
        AgentHandle from,
        AgentId toId,
        string toName,
        MailMessage mail,
        bool isReply)
    {
        lifecycle.Publish(new MailSent(from.Name, toName, mail.Id, mail.Subject, mail.Body, isReply));

        if (toId.IsUser)
        {
            // User bridge observes MailSent; nothing to inbox.
            return isReply
                ? $"Reply delivered to '{toName}', mailId={mail.Id}."
                : $"Sent to '{toName}', mailId={mail.Id}.";
        }

        if (!registry.TryGet(toId, out var to))
        {
            return $"Recipient '{toName}' is gone.";
        }

        DeliverToAgent(to, mail);
        return isReply
            ? $"Reply delivered to '{toName}', mailId={mail.Id}."
            : $"Sent to '{toName}', mailId={mail.Id}.";
    }

    private void DeliverToAgent(AgentHandle to, MailMessage mail)
    {
        if (to.State == AgentRunState.Disposed)
        {
            return;
        }

        to.Inbox.Add(mail);
        lifecycle.Publish(new MailReceived(
            to.Name,
            mail.Id,
            mail.From,
            mail.Subject,
            mail.IsFromParent,
            mail.Timestamp));

        var notice = new MailNotice(mail.Id, mail.Timestamp, mail.From, mail.Subject, mail.IsFromParent);
        if (to.State == AgentRunState.Running)
        {
            to.EnqueueMailNotice(notice);
        }
        else
        {
            to.RequestWake();
        }
    }

    private MailMessage CreateMail(
        string from,
        string to,
        string subject,
        string body,
        bool isFromParent,
        Guid threadId)
    {
        var seq = Interlocked.Increment(ref _mailSeq);
        return new MailMessage
        {
            Id = $"m{seq}",
            Timestamp = DateTime.UtcNow,
            From = from,
            To = to,
            Subject = subject,
            Body = body,
            IsFromParent = isFromParent,
            ThreadId = threadId
        };
    }

    private bool TryResolveRecipient(
        AgentHandle from,
        string toName,
        out AgentId toId,
        out string error)
    {
        toId = default;
        error = string.Empty;
        toName = toName.Trim();

        if (from.ParentId.IsUser
            && string.Equals(toName, AgentId.User.Value, StringComparison.Ordinal))
        {
            toId = AgentId.User;
            return true;
        }

        if (!from.ParentId.IsUser
            && registry.TryGet(from.ParentId, out var parent)
            && string.Equals(toName, parent.Name, StringComparison.Ordinal))
        {
            toId = parent.Id;
            return true;
        }

        if (from.ChildrenByName.TryGetValue(toName, out var childId))
        {
            toId = childId;
            return true;
        }

        error =
            $"Recipient '{toName}' is not addressable. " +
            "You may only mail your parent and direct subagents.";
        return false;
    }

    private string ResolveDisplayName(AgentId id, string fallback)
    {
        if (id.IsUser)
        {
            return AgentId.User.Value;
        }

        return registry.TryGet(id, out var handle) ? handle.Name : fallback;
    }

    private bool IsParentOf(AgentId fromId, AgentId toId)
    {
        if (toId.IsUser)
        {
            return false;
        }

        return registry.TryGet(toId, out var to) && to.ParentId.Equals(fromId);
    }
}
