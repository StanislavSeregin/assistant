using Assistant.App.Lifecycle;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Assistant.App.Mail;

public sealed class MailService(
    NodeRegistry registry,
    ILifecycleSink lifecycle,
    IAgentStateStore store)
{
    private long _mailSeq;

    public void SetMailSeq(long mailSeq) => Interlocked.Exchange(ref _mailSeq, mailSeq);

    public IReadOnlyList<(string Name, string Role, bool IsParent)> GetRecipients(NodeHandle node)
    {
        var list = new List<(string, string, bool)>();

        if (node.ParentId is { } parentId)
        {
            if (parentId.IsUser)
            {
                list.Add((
                    NodeId.User.Value,
                    node.ParentDescription ?? "User",
                    true));
            }
            else if (registry.TryGet(parentId, out var parent))
            {
                list.Add((parent.Name, parent.Description, true));
            }
        }

        foreach (var (name, childId) in node.ChildrenByName.OrderBy(p => p.Key, StringComparer.Ordinal))
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

    public IReadOnlyList<InboxItem> ListInbox(NodeHandle node) => node.Inbox.List();

    public string ReadMail(NodeHandle node, string mailId)
    {
        var message = node.Inbox.FindAndMarkRead(mailId);
        if (message is null)
        {
            return $"Mail '{mailId}' not found.";
        }

        PersistInbox(node);
        lifecycle.Publish(new MailRead(
            node.Name,
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

    public MailMessage? TryGetMail(NodeHandle node, string mailId) => node.Inbox.Find(mailId);

    public (bool Ok, string Message) WriteMail(NodeHandle from, string toName, string subject, string body)
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

    public (bool Ok, string Message) ReplyMail(NodeHandle from, string mailId, string body)
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

        PersistInbox(from);
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

    public (bool Ok, string Message) DeleteMail(NodeHandle node, string mailId)
    {
        if (!node.Inbox.TryRemove(mailId, out _))
        {
            return (false, $"Mail '{mailId}' not found.");
        }

        PersistInbox(node);
        lifecycle.Publish(new MailDeleted(node.Name, mailId));
        return (true, $"Deleted mail '{mailId}' from inbox.");
    }

    public int PurgeMailFrom(NodeHandle node, IEnumerable<string> fromNames)
    {
        var total = 0;
        foreach (var name in fromNames.Distinct(StringComparer.Ordinal))
        {
            var count = node.Inbox.PurgeFrom(name);
            if (count > 0)
            {
                total += count;
                lifecycle.Publish(new MailPurged(node.Name, name, count));
            }
        }

        if (total > 0)
        {
            PersistInbox(node);
        }

        return total;
    }

    private string Deliver(
        NodeHandle from,
        NodeId toId,
        string toName,
        MailMessage mail,
        bool isReply)
    {
        lifecycle.Publish(new MailSent(from.Name, toName, mail.Id, mail.Subject, mail.Body, isReply));

        if (!registry.TryGet(toId, out var to) || to.IsDisposed)
        {
            return $"Recipient '{toName}' is gone.";
        }

        DeliverToNode(to, mail);
        return isReply
            ? $"Reply delivered to '{toName}', mailId={mail.Id}."
            : $"Sent to '{toName}', mailId={mail.Id}.";
    }

    private void DeliverToNode(NodeHandle to, MailMessage mail)
    {
        to.Inbox.Add(mail);
        PersistInbox(to);
        lifecycle.Publish(new MailReceived(
            to.Name,
            mail.Id,
            mail.From,
            mail.Subject,
            mail.IsFromParent,
            mail.Timestamp));

        if (to.Llm is null)
        {
            // Human (and other non-LLM) nodes: inbox + event only; UI reacts.
            return;
        }

        var notice = new SystemNotification(
            SystemNotificationKinds.Mail,
            $"id={mail.Id}; time={MailTimestamp.FormatUtc(mail.Timestamp)}; from={mail.From}; subject={mail.Subject}",
            mail.Id);
        if (to.Llm.State == NodeRunState.Running)
        {
            to.Llm.EnqueueSystemNotification(notice);
        }
        else
        {
            to.Llm.RequestWake();
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
        store.SaveMailSeq(seq);
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

    private void PersistInbox(NodeHandle node) => store.SaveInbox(node);

    private bool TryResolveRecipient(
        NodeHandle from,
        string toName,
        out NodeId toId,
        out string error)
    {
        toId = default;
        error = string.Empty;
        toName = toName.Trim();

        if (from.ParentId is { } parentId)
        {
            if (parentId.IsUser
                && string.Equals(toName, NodeId.User.Value, StringComparison.Ordinal))
            {
                toId = NodeId.User;
                return true;
            }

            if (!parentId.IsUser
                && registry.TryGet(parentId, out var parent)
                && string.Equals(toName, parent.Name, StringComparison.Ordinal))
            {
                toId = parent.Id;
                return true;
            }
        }

        if (from.ChildrenByName.TryGetValue(toName, out var childId))
        {
            toId = childId;
            return true;
        }

        error =
            $"Recipient '{toName}' is not addressable. " +
            "You may only mail your parent and direct children.";
        return false;
    }

    private string ResolveDisplayName(NodeId id, string fallback)
    {
        return registry.TryGet(id, out var handle) ? handle.Name : fallback;
    }

    private bool IsParentOf(NodeId fromId, NodeId toId)
    {
        return registry.TryGet(toId, out var to)
            && to.ParentId is { } parentId
            && parentId.Equals(fromId);
    }
}
