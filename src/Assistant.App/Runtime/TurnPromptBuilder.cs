using Assistant.App.Checklist;
using Assistant.App.Mail;
using Assistant.App.Registry;
using Assistant.App.Support;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Assistant.App.Runtime;

internal static class TurnPromptBuilder
{
    public static (ChatMessage Message, IReadOnlyList<string> ListedMailIds) BuildWakeMessage(
        NodeHandle agent)
    {
        var open = agent.Inbox.List();
        var checklist = agent.Llm?.Checklist.Snapshot() ?? Array.Empty<ChecklistItem>();
        if (open.Count == 0 && checklist.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{agent.Name}' started a turn with an empty inbox and empty checklist.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("[SYSTEM]");

        var handoff = agent.Llm?.ContinuityHandoff;
        if (!string.IsNullOrWhiteSpace(handoff))
        {
            sb.AppendLine("Continuity note from last episode:");
            sb.AppendLine(handoff.Trim());
            sb.AppendLine();
        }

        if (open.Count == 0)
        {
            sb.AppendLine("Mail waiting: (none)");
        }
        else
        {
            sb.AppendLine("Mail waiting (ReplyMail / WriteMail deliver):");
            foreach (var item in open)
            {
                sb.Append("- id=")
                    .Append(item.Id)
                    .Append("; time=")
                    .Append(MailTimestamp.FormatUtc(item.Timestamp))
                    .Append("; from=")
                    .Append(item.From)
                    .Append("; subject=")
                    .Append(item.Subject)
                    .Append(MailStatusDisplay.ListSuffix(item.Status))
                    .AppendLine();
            }
        }

        sb.AppendLine();
        AppendChecklistBlock(sb, checklist);

        sb.AppendLine();
        sb.Append(ContinuityHandoffGuide.WakeFooter);

        var listedIds = open.Select(item => item.Id).ToArray();
        return (new ChatMessage(ChatRole.User, sb.ToString()), listedIds);
    }

    public static ChatMessage BuildCompactNudgeMessage()
    {
        var body = "[SYSTEM]" + Environment.NewLine + ContinuityHandoffGuide.CompactNudgeBody;
        return new ChatMessage(ChatRole.User, body);
    }

    public static string BuildFallbackProgressNotice(NodeHandle agent)
    {
        var open = agent.Inbox.List();
        var checklist = agent.Llm?.Checklist.Snapshot() ?? Array.Empty<ChecklistItem>();
        var parentMail = open.FirstOrDefault(item => item.IsFromParent) ?? open.FirstOrDefault();

        if (checklist.Count > 0)
        {
            var first = checklist[0];
            return $"Episode incomplete: open checklist (e.g. id={first.Id}). " +
                   "Advance the plan with Checklist* or send mail — thinking is not delivery.";
        }

        if (parentMail is { IsFromParent: true })
        {
            return $"Episode incomplete: parent mail id={parentMail.Id} " +
                   $"subject={parentMail.Subject} is still open. " +
                   "ReplyMail that id with the final answer — thinking is not delivery.";
        }

        if (parentMail is not null)
        {
            return $"Episode incomplete: inbox still has id={parentMail.Id} from={parentMail.From}. " +
                   "ReplyMail / WriteMail / DeleteMail, or update Checklist*.";
        }

        return "Episode incomplete: call WriteMail or ReplyMail — thinking is not delivery.";
    }

    public static string BuildFallbackCompactNotice() =>
        ContinuityHandoffGuide.FallbackCompactNotice;

    private static void AppendChecklistBlock(StringBuilder sb, IReadOnlyList<ChecklistItem> checklist)
    {
        sb.AppendLine("Open checklist (ChecklistSet / Add / Complete / Remove):");
        if (checklist.Count == 0)
        {
            sb.AppendLine("- (none)");
            return;
        }

        foreach (var item in checklist)
        {
            sb.Append("- id=")
                .Append(item.Id)
                .Append("; text=")
                .Append(item.Text)
                .AppendLine();
        }
    }
}
