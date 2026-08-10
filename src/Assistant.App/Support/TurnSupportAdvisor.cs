using Assistant.App.Mail;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Support;

public enum TurnSupportMode
{
    Progress,
    Compact
}

/// <summary>
/// Stateless advisor injected as a [SYSTEM] turn notice when a phase stalls.
/// </summary>
public sealed class TurnSupportAdvisor(StatelessAgent stateless, ModelActivityTracker activity)
{
    private const int MaxAdviceOutputTokens = 160;
    private const int MaxAdviceChars = 550;

    private const string ProgressInstructions = """
        An agent has not completed episode progress yet: needs a mail tool
        (ReplyMail / WriteMail / DeleteMail, or DisposeSubagent that purged mail)
        or a Checklist* change that alters the open plan.

        Write the BODY of a runtime [SYSTEM] notice (no [SYSTEM] prefix). Not mail.
        No markdown, no lists, no persona ("support", "I", "we").

        Read inbox + checklist + transcript; name one concrete next tool.
        - Multi-step work, empty plan: ChecklistSet ordered steps (one outcome per item);
          this episode advance about one item.
        - Finished an open item: ChecklistComplete that id (unknown id soft-fails).
        - Plan empty, parent mail still open: ReplyMail final answer to that id.
        - Optional mid-work status to parent: WriteMail (does not close the ask).
        - Finished child report: integrate, DisposeSubagent — no ReplyMail to the child
          unless they asked a clarifying question.
        - Clarifying question from child: ReplyMail the answer.
        - Leftover finished-child mail: DisposeSubagent and/or DeleteMail.
        - Answer only in thinking: send it with ReplyMail / WriteMail now.
        - Earlier notice failed: different concrete exit.

        Be specific (ids, names). A few sentences, ~550 characters max.
        """;

    private const string CompactInstructions = """
        Episode progress is done; CommitContext is still needed. It saves a short continuity
        note, then clears history; inbox and checklist return next episode.

        Write the BODY of a runtime [SYSTEM] notice (no [SYSTEM] prefix). Not mail.
        Calm, brief — no markdown, no persona ("support", "I", "we"). Do not restart progress work.

        - Note only in thinking: CommitContext with that content.
        - Soft error (empty note / progress not settled): say the fix, invite retry.
        - Unsure: sections Waiting; Open ask; Facts to keep — briefing, not a todo list.
        - Earlier notice failed: different nudge.

        A few sentences, ~550 characters max.
        """;

    public async Task<string> AdviseAsync(
        TurnSupportMode mode,
        NodeHandle agent,
        int historyStartIndex,
        int attempt,
        CancellationToken cancellationToken)
    {
        var package = BuildPackage(mode, agent, historyStartIndex, attempt);
        var instructions = mode == TurnSupportMode.Progress ? ProgressInstructions : CompactInstructions;
        using (activity.Enter(agent.Name, ModelActivityActors.Support))
        {
            var advice = await stateless.RunAsync(
                instructions,
                package,
                cancellationToken,
                MaxAdviceOutputTokens);
            return CompactAdvice(advice);
        }
    }

    private static string CompactAdvice(string advice)
    {
        if (string.IsNullOrWhiteSpace(advice))
        {
            return string.Empty;
        }

        var text = advice.Trim();
        if (text.StartsWith("[SYSTEM]", StringComparison.OrdinalIgnoreCase))
        {
            text = text["[SYSTEM]".Length..].TrimStart();
        }
        else if (text.StartsWith("[SUPPORT]", StringComparison.OrdinalIgnoreCase))
        {
            text = text["[SUPPORT]".Length..].TrimStart();
        }

        if (text.Length <= MaxAdviceChars)
        {
            return text;
        }

        return text[..MaxAdviceChars].TrimEnd() + "…";
    }

    private static string BuildPackage(
        TurnSupportMode mode,
        NodeHandle agent,
        int historyStartIndex,
        int attempt)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"attempt={attempt}; agent={agent.Name}; parent={agent.ParentId?.Value ?? "(none)"}");

        if (mode == TurnSupportMode.Progress)
        {
            sb.AppendLine("Open inbox now:");
            var open = agent.Inbox.List();
            if (open.Count == 0)
            {
                sb.AppendLine("(empty)");
            }
            else
            {
                foreach (var item in open)
                {
                    sb.Append("- id=")
                        .Append(item.Id)
                        .Append("; from=")
                        .Append(item.From)
                        .Append("; subject=")
                        .Append(item.Subject)
                        .Append("; parent=")
                        .Append(item.IsFromParent ? "yes" : "no")
                        .Append(MailStatusDisplay.ListSuffix(item.Status))
                        .AppendLine();
                }
            }

            sb.AppendLine("Open checklist now:");
            var checklist = agent.Llm?.Checklist.Snapshot();
            if (checklist is null || checklist.Count == 0)
            {
                sb.AppendLine("(empty)");
            }
            else
            {
                foreach (var item in checklist)
                {
                    sb.Append("- id=")
                        .Append(item.Id)
                        .Append("; text=")
                        .Append(item.Text)
                        .AppendLine();
                }
            }

            sb.AppendLine("Turn transcript:");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(agent.Llm?.ContinuityHandoff))
            {
                sb.AppendLine(
                    "Prior committed handoff (previous turns) exists; this episode still needs a new CommitContext.");
            }

            sb.AppendLine("Compact-phase transcript:");
        }

        sb.Append(FormatTurnHistory(agent, historyStartIndex, mode));
        return sb.ToString();
    }

    private static string FormatTurnHistory(
        NodeHandle agent,
        int historyStartIndex,
        TurnSupportMode mode)
    {
        if (agent.Llm?.Session is null
            || !agent.Llm.Session.TryGetInMemoryChatHistory(out var history)
            || history.Count == 0)
        {
            return "(session history unavailable)";
        }

        var start = Math.Clamp(historyStartIndex, 0, history.Count);
        if (start >= history.Count)
        {
            return mode == TurnSupportMode.Progress
                ? "(no messages recorded for this episode yet)"
                : "(no messages recorded for this compact phase yet)";
        }

        if (start == 0)
        {
            return TurnHistoryFormatter.Format(history);
        }

        var slice = new List<ChatMessage>(history.Count - start);
        for (var i = start; i < history.Count; i++)
        {
            slice.Add(history[i]);
        }

        return TurnHistoryFormatter.Format(slice);
    }
}
