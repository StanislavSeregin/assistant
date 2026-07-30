using System;
using System.Collections.Generic;
using System.Linq;

namespace AssistantV2.App.Mail;

public sealed class MailInbox
{
    private readonly object _sync = new();
    private readonly List<MailMessage> _messages = [];

    public void Add(MailMessage message)
    {
        lock (_sync)
        {
            _messages.Add(message);
        }
    }

    public IReadOnlyList<InboxItem> List()
    {
        lock (_sync)
        {
            return _messages
                .OrderBy(m => m.Timestamp)
                .Select(m => new InboxItem(
                    m.Id,
                    m.Timestamp,
                    m.From,
                    m.Subject,
                    m.IsFromParent,
                    m.Status))
                .ToArray();
        }
    }

    public bool HasMail()
    {
        lock (_sync)
        {
            return _messages.Count > 0;
        }
    }

    public MailMessage? Find(string id)
    {
        lock (_sync)
        {
            return _messages.FirstOrDefault(m => m.Id == id);
        }
    }

    public MailMessage? FindAndMarkRead(string id)
    {
        lock (_sync)
        {
            var message = _messages.FirstOrDefault(m => m.Id == id);
            if (message is not null)
            {
                message.Status = MailStatus.Read;
            }

            return message;
        }
    }

    public bool TryRemove(string id, out MailMessage? message)
    {
        lock (_sync)
        {
            var index = _messages.FindIndex(m => m.Id == id);
            if (index < 0)
            {
                message = null;
                return false;
            }

            message = _messages[index];
            _messages.RemoveAt(index);
            return true;
        }
    }

    public int PurgeFrom(string fromName)
    {
        lock (_sync)
        {
            var removed = _messages.RemoveAll(m =>
                string.Equals(m.From, fromName, StringComparison.Ordinal));
            return removed;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _messages.Clear();
        }
    }
}
