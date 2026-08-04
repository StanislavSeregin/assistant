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
        if (open.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{agent.Name}' started a turn with an empty inbox.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("[SYSTEM]");
        sb.AppendLine("Your continuity note from last wake:");
        var handoff = agent.Llm?.ContinuityHandoff;
        if (string.IsNullOrWhiteSpace(handoff))
        {
            sb.AppendLine("(fresh start — nothing saved yet)");
        }
        else
        {
            sb.AppendLine(handoff.Trim());
        }

        sb.AppendLine();
        sb.AppendLine(
            "Mail waiting for you (ReplyMail / WriteMail are how others see your answer):");
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

        sb.AppendLine();
        sb.AppendLine("How this wake works:");
        sb.AppendLine(
            "Handle the mail first. When that is done, call CommitContext with a continuity " +
            "note for your next wake — you can do it in the same breath as the mail tools.");
        sb.Append(
            "After CommitContext, chat history is cleared so the next wake starts from your " +
            "note (as reference) plus the inbox. Treat this wake as one focused stretch of work.");

        var listedIds = open.Select(item => item.Id).ToArray();
        return (new ChatMessage(ChatRole.User, sb.ToString()), listedIds);
    }

    public static ChatMessage BuildCompactNudgeMessage()
    {
        var body = "[SYSTEM]" + Environment.NewLine + ContinuityHandoffGuide.CompactNudgeBody;
        return new ChatMessage(ChatRole.User, body);
    }

    public static string BuildFallbackMailNotice(NodeHandle agent)
    {
        var open = agent.Inbox.List();
        var parentMail = open.FirstOrDefault(item => item.IsFromParent) ?? open.FirstOrDefault();
        if (parentMail is null)
        {
            return "Turn incomplete: no mail was delivered. " +
                   "Call WriteMail or ReplyMail — free text is private and reaches no one.";
        }

        return $"Turn incomplete: inbox still has id={parentMail.Id} from={parentMail.From} " +
               $"subject={parentMail.Subject}. ReplyMail to that id with your answer — " +
               "free text is private and reaches no one.";
    }

    public static string BuildFallbackCompactNotice() =>
        ContinuityHandoffGuide.FallbackCompactNotice;
}
