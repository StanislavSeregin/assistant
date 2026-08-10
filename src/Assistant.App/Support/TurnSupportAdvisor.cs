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
    Mail,
    Compact
}

/// <summary>
/// Stateless advisor injected as a [SYSTEM] turn notice when a phase stalls.
/// </summary>
public sealed class TurnSupportAdvisor(StatelessAgent stateless, ModelActivityTracker activity)
{
    private const int MaxAdviceOutputTokens = 160;
    private const int MaxAdviceChars = 550;

    private const string MailInstructions = """
        An agent did not finish its turn: no successful ReplyMail, WriteMail, or DeleteMail
        (and no DisposeSubagent that purged child mail) this turn.

        Write the BODY of a runtime [SYSTEM] notice (no [SYSTEM] prefix). Not mail.
        No markdown, no lists, no persona ("support", "I", "we").

        Untangle THIS turn: read inbox snapshot + transcript; pick the real exit.
        - Already done / leftover inbox: DeleteMail leftover id(s) and/or DisposeSubagent
          finished children — do not redo the task.
        - Outbound only in thinking: ReplyMail / WriteMail that text now (id/from/subject).
          Defer parent reply while waiting on children is fine.
        - ReadMail without clearing: ReplyMail or DeleteMail that id.
        - Earlier [SYSTEM] failed: different concrete exit.

        Be specific (ids, names). A few sentences, ~550 characters max.
        """;

    private const string CompactInstructions = """
        Mail work is done but CommitContext was not called. CommitContext saves a short
        continuity note for the next wake; then history clears.

        Write the BODY of a runtime [SYSTEM] notice (no [SYSTEM] prefix). Not mail.
        Calm, brief — no markdown, no persona ("support", "I", "we"). Do not restart mail work.

        - Note only in thinking: CommitContext with that content.
        - Soft error (empty / mail not settled): say the fix, invite retry.
        - Unsure what to write: sections Waiting; Open ask; Facts to keep; Next step —
          fill what applies; briefing not transcript; inbox returns on wake; note is reference.
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
        var instructions = mode == TurnSupportMode.Mail ? MailInstructions : CompactInstructions;
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

        if (mode == TurnSupportMode.Mail)
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

            sb.AppendLine("Turn transcript:");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(agent.Llm?.ContinuityHandoff))
            {
                sb.AppendLine(
                    "Prior committed handoff (previous turns) exists; this turn still needs a new CommitContext.");
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
            return mode == TurnSupportMode.Mail
                ? "(no messages recorded for this turn yet)"
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
