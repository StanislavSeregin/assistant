using Assistant.App.Checklist;
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
        nameof(ChecklistSet),
        nameof(ChecklistAdd),
        nameof(ChecklistComplete),
        nameof(ChecklistRemove),
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
                "Send a new mail to an addressable recipient. " +
                "Use for briefs, child traffic, or optional mid-work progress to the parent."),
            AIFunctionFactory.Create(
                (string mailId, string body) => ReplyMail(agent, activity, mailId, body),
                nameof(ReplyMail),
                "Reply to an inbox mail by id with your new text only — prior thread is appended. " +
                "On parent ask: final ReplyMail closes that ask when the work is finished."),
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
                [Description("Replace the open checklist with these actionable steps (in order).")]
                (
                    [Description("Actionable remaining steps, in order")]
                    string[] items) =>
                    ChecklistSet(agent, activity, items),
                nameof(ChecklistSet)),
            AIFunctionFactory.Create(
                [Description("Append actionable steps to the open checklist.")]
                (
                    [Description("New actionable steps to append")]
                    string[] items) =>
                    ChecklistAdd(agent, activity, items),
                nameof(ChecklistAdd)),
            AIFunctionFactory.Create(
                [Description(
                    "Mark an open checklist item done by id (removes it). " +
                    "Unknown id → soft fail, plan unchanged.")]
                (
                    [Description("Open checklist item id from wake")]
                    string id) =>
                    ChecklistComplete(agent, activity, id),
                nameof(ChecklistComplete)),
            AIFunctionFactory.Create(
                [Description(
                    "Drop an open checklist item by id (replan, not done). " +
                    "Unknown id → soft fail, plan unchanged.")]
                (
                    [Description("Open checklist item id from wake")]
                    string id) =>
                    ChecklistRemove(agent, activity, id),
                nameof(ChecklistRemove)),
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

    private static string ChecklistSet(NodeHandle agent, TurnActivity activity, string[] items)
    {
        var checklist = RequireChecklist(agent);
        if (!checklist.Set(items ?? []))
        {
            return "Checklist unchanged.";
        }

        activity.MarkChecklistMutated();
        return FormatChecklistResult(checklist, "Checklist replaced.");
    }

    private static string ChecklistAdd(NodeHandle agent, TurnActivity activity, string[] items)
    {
        var checklist = RequireChecklist(agent);
        if (!checklist.Add(items ?? []))
        {
            return "Nothing to add.";
        }

        activity.MarkChecklistMutated();
        return FormatChecklistResult(checklist, "Checklist items added.");
    }

    private static string ChecklistComplete(NodeHandle agent, TurnActivity activity, string id)
    {
        var checklist = RequireChecklist(agent);
        if (!checklist.Complete(id))
        {
            return $"No open checklist item id={id}.";
        }

        activity.MarkChecklistMutated();
        return FormatChecklistResult(checklist, $"Completed id={id}.");
    }

    private static string ChecklistRemove(NodeHandle agent, TurnActivity activity, string id)
    {
        var checklist = RequireChecklist(agent);
        if (!checklist.Remove(id))
        {
            return $"No open checklist item id={id}.";
        }

        activity.MarkChecklistMutated();
        return FormatChecklistResult(checklist, $"Removed id={id}.");
    }

    private static AgentChecklist RequireChecklist(NodeHandle agent) =>
        agent.Llm?.Checklist
        ?? throw new InvalidOperationException($"Node '{agent.Name}' has no checklist.");

    private static string FormatChecklistResult(AgentChecklist checklist, string preface)
    {
        var open = checklist.Snapshot();
        if (open.Count == 0)
        {
            return $"{preface} Open checklist: (empty).";
        }

        var sb = new StringBuilder();
        sb.Append(preface).Append(" Open checklist:");
        foreach (var item in open)
        {
            sb.AppendLine().Append("- id=").Append(item.Id).Append("; text=").Append(item.Text);
        }

        return sb.ToString();
    }

    private string CommitContext(NodeHandle agent, TurnActivity activity, string handoff)
    {
        if (!activity.AllowContextCommit)
        {
            return "Almost — finish episode progress first " +
                   "(mail tool, or Checklist* change that alters the plan), " +
                   "then CommitContext. History is still intact.";
        }

        if (string.IsNullOrWhiteSpace(handoff))
        {
            return "CommitContext needs a non-empty continuity note for your next wake.";
        }

        var text = handoff.Trim();
        if (agent.Llm?.Session is null)
        {
            return "CommitContext could not run: session is not bound.";
        }

        agent.Llm.ContinuityHandoff = text;
        agent.Llm.Session.SetInMemoryChatHistory([]);
        activity.MarkContextCommitted();
        checkpoint.CheckpointClearedSession(agent);
        lifecycle.Publish(new ContextCommitted(agent.Name, text, text.Length));

        if (TurnContinuation.ShouldContinueInSlot(agent))
        {
            return "Saved. History cleared. Next episode opens with this note, inbox, and checklist.";
        }

        return "Saved. History cleared. " + ContinuityHandoffGuide.DoneWhenBlurb;
    }
}
