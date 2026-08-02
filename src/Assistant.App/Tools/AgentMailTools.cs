using Assistant.App.Mail;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Assistant.App.Tools;

public sealed class AgentMailTools(
    MailService mail,
    AgentRegistry registry,
    AgentBootstrap bootstrap)
{
    public static readonly HashSet<string> ApplicationToolNames =
    [
        nameof(GetRecipients),
        nameof(ListInbox),
        nameof(ReadMail),
        nameof(WriteMail),
        nameof(ReplyMail),
        nameof(DeleteMail),
        nameof(SpawnSubagent),
        nameof(DisposeSubagent)
    ];

    public AITool[] BuildTools(AgentHandle agent, TurnActivity activity) =>
    [
        AIFunctionFactory.Create(
            () => GetRecipients(agent),
            nameof(GetRecipients),
            "Get addressable recipients: your parent and direct subagents."),
        AIFunctionFactory.Create(
            () => ListInbox(agent),
            nameof(ListInbox),
            "List inbox mail chronologically. Returns id, time, from, subject; unread mail also show status=NEW."),
        AIFunctionFactory.Create(
            (string mailId) => ReadMail(agent, mailId),
            nameof(ReadMail),
            "Read a mail by id from your inbox. Clears its NEW marker."),
        AIFunctionFactory.Create(
            (string to, string subject, string body) => WriteMail(agent, activity, to, subject, body),
            nameof(WriteMail),
            "Send a new mail to an addressable recipient."),
        AIFunctionFactory.Create(
            (string mailId, string body) => ReplyMail(agent, activity, mailId, body),
            nameof(ReplyMail),
            "Reply to an inbox mail by id. This is how the sender receives your answer."),
        AIFunctionFactory.Create(
            (string mailId) => DeleteMail(agent, activity, mailId),
            nameof(DeleteMail),
            "Discard an inbox mail without answering."),
        AIFunctionFactory.Create(
            [Description(
                "Spawn a direct subagent (identity only). Load skill subagent-management first. " +
                "Then WriteMail the concrete ask — never put this turn's task in description/instructions. " +
                "parentRole = your duty as their manager (not your name; shown to the child).")]
            (
                [Description("Unique name among your subagents (e.g. Researcher, NumberPicker)")]
                string name,
                [Description(
                    "Stable specialty — who they are across tasks. NOT this turn's ask " +
                    "(e.g. 'Picks numbers when asked', not 'Pick a number 1-10').")]
                string description,
                [Description(
                    "Standing style and constraints only. NOT steps for this one ask — " +
                    "put the ask in WriteMail after spawn.")]
                string instructions,
                [Description("Your duty as their manager (e.g. Manager), not your name")]
                string parentRole) =>
                SpawnSubagent(agent, name, description, instructions, parentRole),
            nameof(SpawnSubagent)),
        AIFunctionFactory.Create(
            (string name) => DisposeSubagent(agent, activity, name),
            nameof(DisposeSubagent),
            "End a direct subagent and its subtree. They are gone: you cannot mail them, " +
            "they cannot mail you, and their mail is removed from your inbox.")
    ];

    private string GetRecipients(AgentHandle agent)
    {
        var recipients = mail.GetRecipients(agent);
        if (recipients.Count == 0)
        {
            return "No addressable recipients.";
        }

        var sb = new StringBuilder();
        foreach (var (name, role, isParent) in recipients)
        {
            sb.AppendLine(
                $"- {name}" +
                (isParent ? " (parent)" : " (subagent)") +
                (string.IsNullOrWhiteSpace(role) ? string.Empty : $": {role}"));
        }

        return sb.ToString().TrimEnd();
    }

    private string ListInbox(AgentHandle agent)
    {
        var items = mail.ListInbox(agent);
        if (items.Count == 0)
        {
            return "Inbox is empty.";
        }

        var sb = new StringBuilder();
        foreach (var item in items)
        {
            sb.AppendLine(
                $"- id={item.Id}; time={MailTimestamp.FormatUtc(item.Timestamp)}; from={item.From}; " +
                $"subject={item.Subject}{MailStatusDisplay.ListSuffix(item.Status)}");
        }

        return sb.ToString().TrimEnd();
    }

    private string ReadMail(AgentHandle agent, string mailId) =>
        mail.ReadMail(agent, mailId);

    private string WriteMail(
        AgentHandle agent,
        TurnActivity activity,
        string to,
        string subject,
        string body)
    {
        var (ok, message) = mail.WriteMail(agent, to, subject, body);
        if (ok)
        {
            activity.MarkMailHandled();
        }

        return message;
    }

    private string ReplyMail(
        AgentHandle agent,
        TurnActivity activity,
        string mailId,
        string body)
    {
        var (ok, message) = mail.ReplyMail(agent, mailId, body);
        if (ok)
        {
            activity.MarkMailHandled();
        }

        return message;
    }

    private string DeleteMail(AgentHandle agent, TurnActivity activity, string mailId)
    {
        var (ok, message) = mail.DeleteMail(agent, mailId);
        if (ok)
        {
            activity.MarkMailHandled();
        }

        return message;
    }

    private string SpawnSubagent(
        AgentHandle parent,
        string name,
        string description,
        string instructions,
        string parentRole)
    {
        try
        {
            var child = registry.SpawnChild(
                parent,
                name,
                description,
                instructions,
                parentRole);
            bootstrap.Bootstrap(child);
            return $"Spawned subagent '{child.Name}'. Write them mail to brief them.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string DisposeSubagent(AgentHandle parent, TurnActivity activity, string name)
    {
        try
        {
            var disposed = registry.DisposeSubtree(parent, name);
            var names = disposed.Select(a => a.Name).ToArray();
            foreach (var handle in disposed)
            {
                handle.Inbox.Clear();
            }

            // Purging wake mail from disposed children is a valid inbox resolution.
            if (mail.PurgeMailFrom(parent, names) > 0)
            {
                activity.MarkMailHandled();
            }

            return $"Disposed '{name}' and subtree ({names.Length} agent(s)).";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
