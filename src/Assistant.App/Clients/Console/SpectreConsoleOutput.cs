using Assistant.App.Interaction;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Assistant.App.Clients.Console;

public sealed class SpectreConsoleOutput(ConsoleGate gate) : IOutputEventHandler
{
    private readonly Dictionary<Guid, ActiveStream> _streams = [];
    private bool _holdingGate;

    public void Handle(IOutputEvent outputEvent)
    {
        if (!_holdingGate)
        {
            gate.Enter();
        }

        try
        {
            Write(outputEvent);
            _holdingGate = _streams.Count > 0;
        }
        finally
        {
            if (!_holdingGate)
            {
                gate.Exit();
            }
        }
    }

    private void Write(IOutputEvent outputEvent)
    {
        switch (outputEvent)
        {
            case ThinkingStarted e:
                OpenStream(e.StreamId, e.Agent, isThinking: true);
                break;
            case ThinkingDelta e:
                WriteDelta(e.StreamId, e.Agent, e.Text, isThinking: true);
                break;
            case ThinkingCompleted e:
                CompleteStream(e.StreamId, e.InputTokens, e.EndedAt);
                break;
            case MessageStarted e:
                PauseThinking(e.From);
                OpenStream(
                    e.StreamId,
                    e.From,
                    isThinking: false,
                    e.To,
                    e.IsForUser,
                    e.StartedAt,
                    e.Kind);
                break;
            case MessageDelta e:
                WriteDelta(e.StreamId, agent: null, e.Text, isThinking: false);
                break;
            case MessageCompleted e:
                CompleteStream(e.StreamId, e.InputTokens);
                break;
            case ToolCalled e:
                PauseThinking(e.Agent);
                WriteNotice(
                    $"[grey]{Markup.Escape(e.Agent)} · {Markup.Escape(e.ToolName)}[/]",
                    FormatToolArguments(e),
                    "grey");
                break;
            case NudgeOutput e:
                PauseThinking(e.Agent);
                WriteNotice($"[yellow]{Markup.Escape(e.Agent)} · nudge[/]", e.Message, "yellow");
                break;
            case CompactionStarted e:
                PauseThinking(e.Agent);
                WriteCompactionStarted(e);
                break;
            case CompactionCompleted e:
                PauseThinking(e.Agent);
                WriteCompactionCompleted(e);
                break;
            case CompactionFailed e:
                PauseThinking(e.Agent);
                WriteNotice(
                    $"[red]{Markup.Escape(e.Agent)} · snapshot failed[/]",
                    e.Message,
                    "red");
                break;
            case ErrorOutput e:
                PauseThinking(e.Agent);
                WriteNotice($"[red]{Markup.Escape(e.Agent)} · error[/]", e.Message, "red");
                break;
            case UsageOutput e:
                PauseThinking(e.Agent);
                WriteUsage(e);
                break;
        }
    }

    private static string? FormatToolArguments(ToolCalled toolCall)
    {
        if (toolCall.Origin != ToolCallOrigin.Application
            || toolCall.Arguments is not { Count: > 0 } arguments)
        {
            return null;
        }

        return string.Join(
            Environment.NewLine,
            arguments.Select(argument => $"{argument.Key}: {argument.Value ?? string.Empty}"));
    }

    private void WriteDelta(Guid streamId, string? agent, string text, bool isThinking)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!_streams.TryGetValue(streamId, out var stream))
        {
            OpenStream(streamId, agent, isThinking);
            stream = _streams[streamId];
        }

        var style = stream.IsThinking
            ? "grey italic"
            : stream.IsForUser || stream.To == "User"
                ? "bold white"
                : "yellow";

        WriteStyledChunk(style, text);
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

    private void OpenStream(
        Guid streamId,
        string? agent,
        bool isThinking,
        string? to = null,
        bool isForUser = false,
        DateTime? startedAt = null,
        MessageKind? kind = null)
    {
        var displayedAt = DateTime.Now;
        var timingStartedAt = startedAt ?? displayedAt;
        _streams[streamId] = new ActiveStream(
            agent,
            timingStartedAt,
            isThinking,
            to,
            isForUser,
            kind);
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            FormatHeader(agent, to, isThinking, isForUser, kind, displayedAt)).LeftJustified());
    }

    private void CompleteStream(Guid streamId, long? inputTokens, DateTime? endedAt = null) =>
        CloseStreamSegment(streamId, inputTokens, endedAt);

    private void PauseThinking(string? agent)
    {
        foreach (var streamId in _streams.Keys.ToList())
        {
            if (!_streams.TryGetValue(streamId, out var stream)
                || !stream.IsThinking
                || agent is not null && stream.Agent != agent)
            {
                continue;
            }

            CloseStreamSegment(streamId);
        }
    }

    private bool CloseStreamSegment(Guid streamId, long? inputTokens = null, DateTime? endedAt = null)
    {
        if (!_streams.Remove(streamId, out var stream))
        {
            return false;
        }

        CloseStyle(stream);
        var ended = endedAt ?? DateTime.Now;
        AnsiConsole.Write(new Rule(FormatFooter(ended, ended - stream.StartedAt, inputTokens)).RightJustified());
        return true;
    }

    private static void CloseStyle(ActiveStream stream)
    {
        if (!stream.HasOpenStyle)
        {
            return;
        }

        AnsiConsole.WriteLine();
        stream.HasOpenStyle = false;
    }

    private static string FormatHeader(
        string? from,
        string? to,
        bool isThinking,
        bool isForUser,
        MessageKind? kind,
        DateTime time)
    {
        var safeFrom = Markup.Escape(from ?? "Unknown");
        var safeTo = Markup.Escape(to ?? string.Empty);
        if (isThinking)
        {
            return $"[grey]{safeFrom} · thinking[/] [grey][[{time:HH:mm:ss}]][/]";
        }

        if (isForUser || to == "User")
        {
            var kindPart = kind is null ? string.Empty : $" · {kind.ToString()!.ToLowerInvariant()}";
            return $"[bold green]{safeFrom} -> You{kindPart}[/] [grey][[{time:HH:mm:ss}]][/]";
        }

        var toPart = string.IsNullOrEmpty(to) ? string.Empty : $" -> {safeTo}";
        var outboundKind = kind is null ? string.Empty : $" · {kind.ToString()!.ToLowerInvariant()}";
        return $"[cyan]{safeFrom}{toPart}{outboundKind}[/] [grey][[{time:HH:mm:ss}]][/]";
    }

    private static string FormatFooter(DateTime endTime, TimeSpan duration, long? inputTokens)
    {
        var seconds = duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        var timing = $"took {seconds}s";
        var usage = UsageFormatter.Format(inputTokens);
        return string.IsNullOrEmpty(usage)
            ? $"[grey][[{endTime:HH:mm:ss}]] ({timing})[/]"
            : $"[grey][[{endTime:HH:mm:ss}]] ({timing}. {usage})[/]";
    }

    private static void WriteNotice(string headerMarkup, string? body, string bodyStyle)
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

        AnsiConsole.Write(new Rule($"[grey][[{DateTime.Now:HH:mm:ss}]][/]").RightJustified());
    }

    private static void WriteCompactionStarted(CompactionStarted message)
    {
        var time = DateTime.Now;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[blue]{Markup.Escape(message.Agent)} · compacting snapshot v{message.Version}[/] " +
            $"[grey]({message.HistoryMessages} messages) [[{time:HH:mm:ss}]][/]").LeftJustified());
    }

    private static void WriteCompactionCompleted(CompactionCompleted message)
    {
        var time = DateTime.Now;
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule(
            $"[blue]{Markup.Escape(message.Agent)} · snapshot v{message.Version}[/] " +
            $"[grey][[{time:HH:mm:ss}]][/]").LeftJustified());

        foreach (var line in message.Snapshot.ReplaceLineEndings("\n").Split('\n'))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(line)}[/]");
        }

        var seconds = message.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture);
        var input = UsageFormatter.Format(message.InputTokens);
        var output = message.OutputTokens is long outputTokens
            ? $"out {FormatCompactNumber(outputTokens)}"
            : string.Empty;
        var usage = string.Join(
            ", ",
            new[] { input, output }.OfType<string>().Where(value => value.Length > 0));
        var metrics =
            $"{message.BeforeMessages} messages -> 1. " +
            $"{FormatCompactNumber(message.BeforeCharacters)} -> " +
            $"{FormatCompactNumber(message.AfterCharacters)} chars";
        if (usage.Length > 0)
        {
            metrics += $". {usage}";
        }

        AnsiConsole.Write(new Rule(
            $"[grey][[{DateTime.Now:HH:mm:ss}]] (took {seconds}s. {metrics})[/]").RightJustified());
    }

    private static string FormatCompactNumber(long value)
    {
        if (value < 1000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
    }

    private static void WriteUsage(UsageOutput message)
    {
        var usage = UsageFormatter.Format(message.InputTokens);
        if (string.IsNullOrEmpty(usage))
        {
            return;
        }

        var time = DateTime.Now;
        AnsiConsole.Write(new Rule(
            $"[grey]{Markup.Escape(message.Agent)} · {usage}[/] " +
            $"[grey][[{time:HH:mm:ss}]][/]").RightJustified());
    }

    private sealed class ActiveStream(
        string? agent,
        DateTime startedAt,
        bool isThinking,
        string? to = null,
        bool isForUser = false,
        MessageKind? kind = null)
    {
        public string? Agent { get; } = agent;
        public DateTime StartedAt { get; } = startedAt;
        public bool IsThinking { get; } = isThinking;
        public string? To { get; } = to;
        public bool IsForUser { get; } = isForUser;
        public MessageKind? Kind { get; } = kind;
        public bool HasOpenStyle { get; set; }
    }
}
