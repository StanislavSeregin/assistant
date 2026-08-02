using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.UI.Abstractions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Assistant.App.UI.Formatting;

/// <summary>
/// Maps lifecycle events to <see cref="ILogSink"/> calls.
/// Toolkit-free: swap Spectre/TUI/etc. by swapping the sink.
/// </summary>
public sealed class LifecycleLogPresenter
{
    private readonly ILogSink _log;
    private readonly Dictionary<Guid, ActiveStream> _streams = [];
    private readonly HashSet<Guid> _activeThinking = [];

    public LifecycleLogPresenter(ILogSink log) => _log = log;

    public bool HasActiveThinking => _activeThinking.Count > 0;

    public void Handle(ILifecycleEvent lifecycleEvent)
    {
        switch (lifecycleEvent)
        {
            case ThinkingStarted e:
                OpenThinking(e.StreamId, e.Agent);
                break;
            case ThinkingDelta e:
                WriteDelta(e.StreamId, e.Agent, e.Text);
                break;
            case ThinkingCompleted e:
                CompleteStream(e.StreamId, e.InputTokens);
                break;
            case MailSent e:
                PauseThinking(e.From);
                WriteMail(e);
                break;
            case MailRead e:
                PauseThinking(e.Agent);
                WriteMailRead(e);
                break;
            case ToolCalled e:
                if (e.ToolName is "ReadMail" or "CommitContext")
                {
                    break;
                }

                PauseThinking(e.Agent);
                WriteNotice(
                    $"{e.Agent} · {FormatToolHeader(e)}",
                    FormatToolBody(e),
                    LogTone.Grey);
                break;
            case ContextCommitted e:
                PauseThinking(e.Agent);
                WriteNotice(
                    $"{e.Agent} · CommitContext",
                    e.Handoff,
                    LogTone.Magenta,
                    writeFooter: false);
                break;
            case TurnWake e:
                WriteNotice($"{e.Agent} · wake", e.Message, LogTone.Grey);
                break;
            case SupportAdvice e:
                PauseThinking(e.Agent);
                WriteNotice($"{e.Agent} · support", e.Message, LogTone.Yellow);
                break;
            case ErrorEvent e:
                PauseThinking(e.Agent);
                WriteNotice($"{e.Agent} · error", e.Message, LogTone.Red);
                break;
            case AgentSpawned e:
                WriteNotice(
                    $"{e.Parent} -> {e.Agent} · spawn",
                    FormatSpawn(e),
                    LogTone.Blue);
                break;
            case AgentDisposed e:
                WriteNotice($"{e.Parent} -> {e.Agent} · dispose", null, LogTone.Blue);
                break;
            case UsageEvent e:
                PauseThinking(e.Agent);
                WriteUsage(e);
                break;
        }
    }

    private void WriteMail(MailSent e)
    {
        var isForUser = e.To == "User";
        var header = isForUser
            ? $"{e.From} -> You · mail"
            : $"{e.From} -> {e.To} · {(e.IsReply ? "reply" : "mail")}";
        var headerTone = isForUser ? LogTone.BoldGreen : LogTone.Cyan;
        var bodyTone = isForUser ? LogTone.BoldWhite : LogTone.Yellow;

        _log.BlankLine();
        _log.Header(header, headerTone, DateTime.Now);
        _log.BodyLine($"id: {e.MailId}", LogTone.Grey);
        _log.BodyLine(e.Subject, LogTone.Grey);
        foreach (var line in e.Body.ReplaceLineEndings("\n").Split('\n'))
        {
            _log.BodyLine(line, bodyTone);
        }

        _log.Footer();
    }

    private void WriteMailRead(MailRead e)
    {
        _log.BlankLine();
        _log.Header($"{e.Agent} · ReadMail from {e.From}", LogTone.Grey, DateTime.Now);
        _log.BodyLine($"id: {e.MailId}", LogTone.Grey);
        _log.BodyLine($"time: {MailTimestamp.FormatUtc(e.Timestamp)}", LogTone.Grey);
        _log.BodyLine(e.Subject, LogTone.Grey);
        _log.Footer();
    }

    private static string FormatToolHeader(ToolCalled toolCall)
    {
        if (IsSkillTool(toolCall.ToolName)
            && TryGetArgument(toolCall, "skillName", out var skillName))
        {
            if (TryGetArgument(toolCall, "resourceName", out var resourceName)
                || TryGetArgument(toolCall, "scriptName", out resourceName))
            {
                return $"{toolCall.ToolName} · {skillName}/{resourceName}";
            }

            return $"{toolCall.ToolName} · {skillName}";
        }

        return toolCall.ToolName;
    }

    private static string? FormatToolBody(ToolCalled toolCall)
    {
        if (IsSkillTool(toolCall.ToolName))
        {
            return null;
        }

        if (toolCall.Arguments is { Count: > 0 } arguments)
        {
            return string.Join(
                Environment.NewLine,
                arguments.Select(argument => $"{argument.Key}: {argument.Value ?? string.Empty}"));
        }

        return string.IsNullOrWhiteSpace(toolCall.Result) ? null : toolCall.Result;
    }

    private static bool IsSkillTool(string toolName) =>
        toolName is "load_skill" or "read_skill_resource" or "run_skill_script";

    private static bool TryGetArgument(ToolCalled toolCall, string key, out string value)
    {
        value = string.Empty;
        if (toolCall.Arguments is null
            || !toolCall.Arguments.TryGetValue(key, out var raw)
            || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw;
        return true;
    }

    private static string FormatSpawn(AgentSpawned e)
    {
        var parentRole = string.IsNullOrWhiteSpace(e.ParentDescription)
            ? "(none)"
            : e.ParentDescription;

        var instructions = string.IsNullOrWhiteSpace(e.Instructions)
            ? "(none)"
            : e.Instructions;

        return string.Join(
            Environment.NewLine,
            [
                $"parentRole: {parentRole}",
                $"description: {e.Description}",
                $"instructions: {instructions}"
            ]);
    }

    private void WriteDelta(Guid streamId, string? agent, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_streams.ContainsKey(streamId))
        {
            OpenThinking(streamId, agent);
        }

        _log.AppendInline(text, LogTone.GreyItalic);
        _streams[streamId].HasOpenStyle = true;
    }

    private void OpenThinking(Guid streamId, string? agent)
    {
        _activeThinking.Add(streamId);
        var displayedAt = DateTime.Now;
        _streams[streamId] = new ActiveStream(agent, displayedAt);
        _log.BlankLine();
        _log.Header($"{agent ?? "?"} · thinking", LogTone.Grey, displayedAt);
    }

    private void CompleteStream(Guid streamId, long? inputTokens)
    {
        CloseStreamSegment(streamId, inputTokens);
        _activeThinking.Remove(streamId);
    }

    private void PauseThinking(string? agent)
    {
        foreach (var streamId in _streams.Keys.ToList())
        {
            if (!_streams.TryGetValue(streamId, out var stream)
                || agent is not null && stream.Agent != agent)
            {
                continue;
            }

            CloseStreamSegment(streamId);
        }
    }

    private bool CloseStreamSegment(Guid streamId, long? inputTokens = null)
    {
        if (!_streams.Remove(streamId, out var stream))
        {
            return false;
        }

        if (stream.HasOpenStyle)
        {
            _log.AppendInline("\n", LogTone.GreyItalic);
        }

        var ended = DateTime.Now;
        var seconds = (ended - stream.StartedAt).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        var usage = UsageFormatter.Format(inputTokens);
        var footer = string.IsNullOrEmpty(usage)
            ? $"(took {seconds}s)"
            : $"(took {seconds}s. {usage})";
        _log.Footer(footer);
        return true;
    }

    private void WriteNotice(
        string header,
        string? body,
        LogTone bodyTone,
        bool writeFooter = true)
    {
        _log.BlankLine();
        _log.Header(header, bodyTone, DateTime.Now);

        if (!string.IsNullOrEmpty(body))
        {
            foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
            {
                _log.BodyLine(line, bodyTone);
            }
        }

        if (writeFooter)
        {
            _log.Footer();
        }
    }

    private void WriteUsage(UsageEvent message)
    {
        var usage = UsageFormatter.Format(message.InputTokens);
        if (string.IsNullOrEmpty(usage))
        {
            return;
        }

        _log.Footer($"{message.Agent} · {usage}");
    }

    private sealed class ActiveStream(string? agent, DateTime startedAt)
    {
        public string? Agent { get; } = agent;
        public DateTime StartedAt { get; } = startedAt;
        public bool HasOpenStyle { get; set; }
    }
}
