using Assistant.App.Mail;
using Assistant.App.Registry;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Support;

/// <summary>
/// Stateless advisor for agents stuck without delivering mail this turn.
/// Output is injected as a [SYSTEM] turn notice (not mail).
/// </summary>
public sealed class MailTurnSupport(StatelessAgent stateless)
{
    private const int MaxAdviceOutputTokens = 160;
    private const int MaxAdviceChars = 550;

    private const string Instructions = """
        An agent failed to finish its turn: no successful ReplyMail, WriteMail, or DeleteMail
        (and no DisposeSubagent that purged child mail) this turn.

        You write the BODY of a runtime [SYSTEM] notice (no [SYSTEM] prefix). Not mail.
        No markdown, no lists, no persona ("support", "I", "we").

        Goal: help this agent untangle the CURRENT situation — the real exit, not a generic
        "send mail" lecture. Read the open-inbox snapshot AND the turn transcript (thinking,
        tool calls, prior [SYSTEM] notices). Infer what already happened.

        Choose the advice that fits:
        - Work already done, leftover inbox / rewake loop, agent frustrated that it "already
          finished": calm it briefly; recommend DeleteMail on the leftover id(s) and/or
          DisposeSubagent for finished children. Do not demand redoing the task.
        - Answer exists only as free text / thinking: point at the waiting mail (id/from/subject)
          and say ReplyMail — free text reaches no one.
        - ReadMail without clearing: ReplyMail or DeleteMail that id.
        - Wrong recipient / parent mail still open: say what to clear or whom to answer.
        - Earlier [SYSTEM] notice failed: try a different concrete exit.

        When you name mail or children, be specific (ids, from, subject, names from the
        transcript). Keep it short: a few compact sentences, ~550 characters max. Prefer
        clarity over a fixed template — structure follows the situation.
        """;

    public async Task<string> AdviseAsync(
        AgentHandle agent,
        int turnHistoryStartIndex,
        int attempt,
        CancellationToken cancellationToken)
    {
        var package = BuildPackage(agent, turnHistoryStartIndex, attempt);
        var advice = await stateless.RunAsync(
            Instructions,
            package,
            cancellationToken,
            MaxAdviceOutputTokens);
        return CompactAdvice(advice);
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

    private static string BuildPackage(AgentHandle agent, int turnHistoryStartIndex, int attempt)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"attempt={attempt}; agent={agent.Name}; parent={agent.ParentId.Value}");
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
        sb.Append(FormatTurnHistory(agent, turnHistoryStartIndex));
        return sb.ToString();
    }

    private static string FormatTurnHistory(AgentHandle agent, int turnHistoryStartIndex)
    {
        if (agent.Session is null
            || !agent.Session.TryGetInMemoryChatHistory(out var history)
            || history.Count == 0)
        {
            return "(session history unavailable)";
        }

        var start = Math.Clamp(turnHistoryStartIndex, 0, history.Count);
        if (start >= history.Count)
        {
            return "(no messages recorded for this turn yet)";
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
