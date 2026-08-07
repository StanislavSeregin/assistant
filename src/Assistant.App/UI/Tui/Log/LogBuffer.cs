using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Formatting;
using System;
using System.Collections.Generic;

namespace Assistant.App.UI.Tui.Log;

public readonly record struct LogDisplayLine(string Text, LogTone Tone, string[]? NameAccents = null);

/// <summary>
/// Ring buffer of display lines with word-wrap. Toolkit-free so tests / alternate UIs can reuse it.
/// </summary>
public sealed class LogBuffer
{
    private readonly List<LogDisplayLine> _lines = [];
    private readonly object _gate = new();
    private readonly int _capacity;
    private int _wrapWidth = 80;
    private bool _inlineOpen;

    public LogBuffer(int capacity = 8_000) =>
        _capacity = Math.Max(256, capacity);

    public int WrapWidth
    {
        get
        {
            lock (_gate)
            {
                return _wrapWidth;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count;
            }
        }
    }

    /// <summary>Snapshot for rendering. Caller must not mutate.</summary>
    public IReadOnlyList<LogDisplayLine> Snapshot()
    {
        lock (_gate)
        {
            return _lines.ToArray();
        }
    }

    public void SetWrapWidth(int width)
    {
        width = Math.Max(8, width);
        lock (_gate)
        {
            if (_wrapWidth == width)
            {
                return;
            }

            _wrapWidth = width;
            RewrapAllUnlocked();
        }
    }

    public void BlankLine()
    {
        lock (_gate)
        {
            CloseInlineUnlocked();
            AddLineUnlocked(string.Empty, LogTone.Dim);
        }
    }

    public void Header(string text, LogTone tone, DateTime timestamp, string[]? nameAccents = null)
    {
        lock (_gate)
        {
            CloseInlineUnlocked();
            AddWrappedUnlocked($"{text} [{timestamp:HH:mm:ss}]", tone, nameAccents);
            // Separator under header
            AddLineUnlocked(new string('─', Math.Min(_wrapWidth, 40)), LogTone.Dim);
        }
    }

    public void BodyLine(string text, LogTone tone)
    {
        lock (_gate)
        {
            CloseInlineUnlocked();
            AddWrappedUnlocked(text, tone);
        }
    }

    public void AppendInline(string text, LogTone tone)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_gate)
        {
            var parts = text.ReplaceLineEndings("\n").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    AppendChunkUnlocked(parts[i], tone);
                }

                if (i < parts.Length - 1)
                {
                    _inlineOpen = false;
                }
            }
        }
    }

    public void Footer(string? text, LogTone tone, string[]? nameAccents = null)
    {
        lock (_gate)
        {
            CloseInlineUnlocked();
            if (string.IsNullOrEmpty(text))
            {
                AddLineUnlocked(new string('─', Math.Min(_wrapWidth, 40)), LogTone.Dim);
            }
            else
            {
                AddWrappedUnlocked(text, tone, nameAccents);
                AddLineUnlocked(new string('─', Math.Min(_wrapWidth, 40)), LogTone.Dim);
            }
        }
    }

    private void CloseInlineUnlocked() => _inlineOpen = false;

    private void AppendChunkUnlocked(string chunk, LogTone tone)
    {
        if (!_inlineOpen || _lines.Count == 0)
        {
            AddWrappedUnlocked(chunk, tone);
            _inlineOpen = true;
            return;
        }

        var last = _lines[^1];
        if (last.Tone != tone)
        {
            AddWrappedUnlocked(chunk, tone);
            _inlineOpen = true;
            return;
        }

        var combined = last.Text + chunk;
        _lines.RemoveAt(_lines.Count - 1);
        AddWrappedUnlocked(combined, tone, last.NameAccents);
        _inlineOpen = true;
    }

    private void AddWrappedUnlocked(string text, LogTone tone, string[]? nameAccents = null)
    {
        foreach (var segment in TextWrapping.Wrap(text, _wrapWidth))
        {
            AddLineUnlocked(segment, tone, nameAccents);
        }
    }

    private void AddLineUnlocked(string text, LogTone tone, string[]? nameAccents = null)
    {
        _lines.Add(new LogDisplayLine(text, tone, nameAccents));
        while (_lines.Count > _capacity)
        {
            _lines.RemoveAt(0);
            _inlineOpen = false;
        }
    }

    private void RewrapAllUnlocked()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        // Conservative rewrap: treat each current line as an atomic segment.
        // Full semantic rewrap would need source blocks; good enough for resize.
        var old = _lines.ToArray();
        _lines.Clear();
        _inlineOpen = false;
        foreach (var line in old)
        {
            if (line.Text.Length == 0)
            {
                AddLineUnlocked(string.Empty, line.Tone);
                continue;
            }

            AddWrappedUnlocked(line.Text, line.Tone, line.NameAccents);
        }
    }
}
