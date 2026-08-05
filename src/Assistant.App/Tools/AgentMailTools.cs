using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Support;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;

namespace Assistant.App.Tools;

public sealed class AgentMailTools(
    MailService mail,
    NodeRegistry registry,
    AgentBootstrap bootstrap,
    ILifecycleSink lifecycle,
    SessionCheckpoint checkpoint)
{
    public const int MaxHandoffCharacters = 4000;

    public static readonly HashSet<string> ApplicationToolNames =
    [
        nameof(GetRecipients),
        nameof(ListInbox),
        nameof(ReadMail),
        nameof(WriteMail),
        nameof(ReplyMail),
        nameof(DeleteMail),
        nameof(SpawnSubagent),
        nameof(DisposeSubagent),
        nameof(CommitContext)
    ];

    public AITool[] BuildTools(NodeHandle agent, TurnActivity activity)
    {
        var tools = new List<AITool>
        {
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
                "Reply to an inbox mail by id with your new answer only — " +
                "prior messages are appended automatically from the mail you are answering."),
            AIFunctionFactory.Create(
                (string mailId) => DeleteMail(agent, activity, mailId),
                nameof(DeleteMail),
                "Discard an inbox mail without answering."),
            AIFunctionFactory.Create(
                [Description(
                    "Spawn a direct subagent (identity only). Load skill subagent-management first. " +
                    "Then WriteMail a declarative brief (WHAT/material/done — not HOW). " +
                    "Never put this turn's ask or procedures in description/instructions. " +
                    "parentRole = your duty as their manager (not your name).")]
                (
                    [Description("Unique name among your subagents (e.g. Researcher, NumberPicker)")]
                    string name,
                    [Description(
                        "Stable specialty — who they are across tasks. NOT this turn's ask.")]
                    string description,
                    [Description(
                        "Short declarative stance/aspirations only. NOT an imperative script " +
                        "or this turn's steps — those go in WriteMail as outcomes, not HOW.")]
                    string instructions,
                    [Description("Your duty as their manager (e.g. Manager), not your name")]
                    string parentRole) =>
                    SpawnSubagent(agent, name, description, instructions, parentRole),
                nameof(SpawnSubagent)),
            AIFunctionFactory.Create(
                (string name) => DisposeSubagent(agent, activity, name),
                nameof(DisposeSubagent),
                "End a direct subagent and its subtree. Prefer this over mailing them a thanks/ACK. " +
                "They are gone: you cannot mail them, they cannot mail you, and their mail " +
                "is removed from your inbox."),
            AIFunctionFactory.Create(
                [Description(ContinuityHandoffGuide.ToolDescription)]
                (
                    [Description(ContinuityHandoffGuide.HandoffArgumentDescription)]
                    string handoff) =>
                    CommitContext(agent, activity, handoff),
                nameof(CommitContext))
        };

        if (bootstrap.ShellTool is { } shellTool)
        {
            tools.Add(shellTool);
        }

        return tools.ToArray();
    }

    private string GetRecipients(NodeHandle agent)
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

    private string ListInbox(NodeHandle agent)
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

    private string ReadMail(NodeHandle agent, string mailId) =>
        mail.ReadMail(agent, mailId);

    private string WriteMail(
        NodeHandle agent,
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
        NodeHandle agent,
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

    private string DeleteMail(NodeHandle agent, TurnActivity activity, string mailId)
    {
        var (ok, message) = mail.DeleteMail(agent, mailId);
        if (ok)
        {
            activity.MarkMailHandled();
        }

        return message;
    }

    private string SpawnSubagent(
        NodeHandle parent,
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
            checkpoint.SaveEmptySession(child);
            return $"Spawned subagent '{child.Name}'. Write them mail to brief them.";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private string DisposeSubagent(NodeHandle parent, TurnActivity activity, string name)
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

    private string CommitContext(NodeHandle agent, TurnActivity activity, string handoff)
    {
        if (!activity.AllowContextCommit)
        {
            return "Almost — settle mail first with ReplyMail, WriteMail, or DeleteMail, " +
                   "then call CommitContext. History is still intact.";
        }

        if (string.IsNullOrWhiteSpace(handoff))
        {
            return "CommitContext needs a short non-empty note for your next wake.";
        }

        var text = handoff.Trim();
        if (text.Length > MaxHandoffCharacters)
        {
            return $"That note is {text.Length} characters (max {MaxHandoffCharacters}). " +
                   "Please shorten it and call CommitContext again — nothing was cleared yet.";
        }

        if (agent.Llm?.Session is null)
        {
            return "CommitContext could not run: session is not bound.";
        }

        agent.Llm.ContinuityHandoff = text;
        agent.Llm.Session.SetInMemoryChatHistory([]);
        activity.MarkContextCommitted();
        checkpoint.CheckpointClearedSession(agent);
        lifecycle.Publish(new ContextCommitted(agent.Name, text, text.Length));
        return "Saved. Chat history cleared. Next wake will open with this note, then your inbox.";
    }
}
