using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Assistant.App.UI.Console;

public sealed class SpectreLifecycleOutput(ConsoleGate gate) : ILifecycleEventHandler
{
    private readonly Dictionary<Guid, ActiveStream> _streams = [];
    private readonly HashSet<Guid> _activeThinking = [];
    private readonly List<LifecycleDrainBarrier> _pendingDrains = [];
    private bool _holdingGate;

    public void Handle(ILifecycleEvent lifecycleEvent)
    {
        if (!_holdingGate)
        {
            gate.Enter();
            _holdingGate = true;
        }

        try
        {
            Write(lifecycleEvent);
        }
        finally
        {
            ReleaseGateIfIdle();
        }
    }

    public void CompleteWhenIdle(LifecycleDrainBarrier barrier)
    {
        if (_holdingGate || _activeThinking.Count > 0)
        {
            _pendingDrains.Add(barrier);
            return;
        }

        barrier.Complete();
    }

    private void ReleaseGateIfIdle()
    {
        if (_activeThinking.Count > 0)
        {
            return;
        }

        if (_holdingGate)
        {
            _holdingGate = false;
            gate.Exit();
        }

        if (_pendingDrains.Count == 0)
        {
            return;
        }

        foreach (var barrier in _pendingDrains)
        {
            barrier.Complete();
        }

        _pendingDrains.Clear();
    }

    private void Write(ILifecycleEvent lifecycleEvent)
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
                // ReadMail is logged via MailRead with full content.
                // CommitContext is logged via ContextCommitted.
                if (e.ToolName is "ReadMail" or "CommitContext")
                {
                    break;
                }

                PauseThinking(e.Agent);
                WriteNotice(
                    $"[grey]{Markup.Escape(e.Agent)} · {Markup.Escape(FormatToolHeader(e))}[/]",
                    FormatToolBody(e),
                    "grey");
                break;
            case ContextCommitted e:
                PauseThinking(e.Agent);
                WriteNotice(
                    $"[magenta]{Markup.Escape(e.Agent)} · CommitContext[/]",
                    e.Handoff,
                    "magenta",
                    writeFooter: false);
                break;
            case TurnWake e:
                WriteNotice($"[grey]{Markup.Escape(e.Agent)} · wake[/]", e.Message, "grey");
                break;
            case SupportAdvice e:
                PauseThinking(e.Agent);
                WriteNotice($"[yellow]{Markup.Escape(e.Agent)} · support[/]", e.Message, "yellow");
                break;
            case ErrorEvent e:
                PauseThinking(e.Agent);
                WriteNotice($"[red]{Markup.Escape(e.Agent)} · error[/]", e.Message, "red");
                break;
            case AgentSpawned e:
                WriteNotice(
                    $"[blue]{Markup.Escape(e.Parent)} -> {Markup.Escape(e.Agent)} · spawn[/]",
                    FormatSpawn(e),
                    "blue");
                break;
            case AgentDisposed e:
                WriteNotice(
                    $"[blue]{Markup.Escape(e.Parent)} -> {Markup.Escape(e.Agent)} · dispose[/]",
                    null,
                    "blue");
                break;
            case UsageEvent e:
                PauseThinking(e.Agent);
                WriteUsage(e);
                break;
        }
    }

    private void WriteMail(MailSent e)
    {
        var time = DateTime.Now;
        var isForUser = e.To == "User";
        var header = isForUser
            ? $"[bold green]{Markup.Escape(e.From)} -> You · mail[/]"
            : $"[cyan]{Markup.Escape(e.From)} -> {Markup.Escape(e.To)} · {(e.IsReply ? "reply" : "mail")}[/]";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"{header} [grey][[{time:HH:mm:ss}]][/]").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]id: {Markup.Escape(e.MailId)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(e.Subject)}[/]");
        foreach (var line in e.Body.ReplaceLineEndings("\n").Split('\n'))
        {
            var style = isForUser ? "bold white" : "yellow";
            AnsiConsole.MarkupLine($"[{style}]{Markup.Escape(line)}[/]");
        }

        WriteBlockFooter();
    }

    private static void WriteMailRead(MailRead e)
    {
        var time = DateTime.Now;
        var header =
            $"[grey]{Markup.Escape(e.Agent)} · ReadMail[/] " +
            $"[grey]from {Markup.Escape(e.From)}[/]";

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"{header} [grey][[{time:HH:mm:ss}]][/]").LeftJustified());
        AnsiConsole.MarkupLine($"[grey]id: {Markup.Escape(e.MailId)}[/]");
        AnsiConsole.MarkupLine($"[grey]time: {MailTimestamp.FormatUtc(e.Timestamp)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(e.Subject)}[/]");
        WriteBlockFooter();
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
        // Skill tools: name is in the header; skip dumping skill content into the console.
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

    private static bool TryGetArgument(
        ToolCalled toolCall,
        string key,
        out string value)
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

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            OpenThinking(streamId, agent);
            stream = _streams[streamId];
        }

        WriteStyledChunk("grey italic", text);
        stream.HasOpenStyle = true;
    }

    private static void WriteStyledChunk(string style, string text)
    {
        var parts = text.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                AnsiConsole.Markup($"[{style}]{Markup.Escape(parts[i])}[/]");
            }

            if (i < parts.Length - 1)
            {
                AnsiConsole.WriteLine();
            }
        }
    }

    private void OpenThinking(Guid streamId, string? agent)
    {
        _activeThinking.Add(streamId);
        var displayedAt = DateTime.Now;
        _streams[streamId] = new ActiveStream(agent, displayedAt);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[grey]{Markup.Escape(agent ?? "?")} · thinking[/] [grey][[{displayedAt:HH:mm:ss}]][/]")
            .LeftJustified());
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
            AnsiConsole.WriteLine();
        }

        var ended = DateTime.Now;
        var seconds = (ended - stream.StartedAt).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        var usage = UsageFormatter.Format(inputTokens);
        var footer = string.IsNullOrEmpty(usage)
            ? $"[grey](took {seconds}s)[/]"
            : $"[grey](took {seconds}s. {usage})[/]";
        WriteBlockFooter(footer);
        return true;
    }

    private static void WriteNotice(
        string headerMarkup,
        string? body,
        string bodyStyle,
        bool writeFooter = true)
    {
        var time = DateTime.Now;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"{headerMarkup} [grey][[{time:HH:mm:ss}]][/]").LeftJustified());

        if (!string.IsNullOrEmpty(body))
        {
            foreach (var line in body.ReplaceLineEndings("\n").Split('\n'))
            {
                AnsiConsole.MarkupLine($"[{bodyStyle}]{Markup.Escape(line)}[/]");
            }
        }

        if (writeFooter)
        {
            WriteBlockFooter();
        }
    }

    private static void WriteBlockFooter(string? titleMarkup = null)
    {
        AnsiConsole.Write(
            string.IsNullOrEmpty(titleMarkup)
                ? new Rule()
                : new Rule(titleMarkup).RightJustified());
    }

    private static void WriteUsage(UsageEvent message)
    {
        var usage = UsageFormatter.Format(message.InputTokens);
        if (string.IsNullOrEmpty(usage))
        {
            return;
        }

        AnsiConsole.Write(new Rule(
            $"[grey]{Markup.Escape(message.Agent)} · {usage}[/]").RightJustified());
    }

    private sealed class ActiveStream(string? agent, DateTime startedAt)
    {
        public string? Agent { get; } = agent;
        public DateTime StartedAt { get; } = startedAt;
        public bool HasOpenStyle { get; set; }
    }
}
