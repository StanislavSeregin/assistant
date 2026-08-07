using Assistant.App.UI.Abstractions;
using System;
using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Assistant.App.UI.Tui.Log;

/// <summary>
/// Virtualized log view: draws only visible rows from <see cref="LogBuffer"/>.
/// </summary>
public sealed class LogPaneView : View
{
    private const int WheelLines = 3;

    private readonly LogBuffer _buffer;
    private readonly CoalescingLogSink _sink;
    private int _lastSeenVersion = -1;
    private bool _followTail = true;
    private int _appliedWrapWidth = -1;

    public LogPaneView(LogBuffer buffer, CoalescingLogSink sink)
    {
        _buffer = buffer;
        _sink = sink;
        CanFocus = true;
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        // Terminal.Gui does not bind wheel/keys to scroll by default — wire them explicitly.
        AddCommand(Command.ScrollUp, () => ScrollBy(-WheelLines));
        AddCommand(Command.ScrollDown, () => ScrollBy(WheelLines));
        AddCommand(Command.PageUp, () => ScrollBy(-Math.Max(1, Viewport.Height)));
        AddCommand(Command.PageDown, () => ScrollBy(Math.Max(1, Viewport.Height)));
        AddCommand(Command.Home, () =>
        {
            _followTail = false;
            return ScrollVertical(-Viewport.Y);
        });
        AddCommand(Command.End, () =>
        {
            _followTail = true;
            ScrollToEnd();
            return true;
        });

        MouseBindings.Add(MouseFlags.WheeledUp, Command.ScrollUp);
        MouseBindings.Add(MouseFlags.WheeledDown, Command.ScrollDown);

        KeyBindings.Add(Key.CursorUp, Command.ScrollUp);
        KeyBindings.Add(Key.CursorDown, Command.ScrollDown);
        KeyBindings.Add(Key.PageUp, Command.PageUp);
        KeyBindings.Add(Key.PageDown, Command.PageDown);
        KeyBindings.Add(Key.Home, Command.Home);
        KeyBindings.Add(Key.End, Command.End);

        _sink.Flushed += OnFlushed;
        ViewportChanged += (_, _) => EnsureWrapWidth();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        EnsureWrapWidth();
        UpdateContentSize();

        var lines = _buffer.Snapshot();
        var height = Math.Max(1, Viewport.Height);
        var width = Math.Max(1, Viewport.Width);

        var first = Math.Clamp(Viewport.Y, 0, Math.Max(0, lines.Count - 1));
        for (var row = 0; row < height; row++)
        {
            var index = first + row;
            Move(0, row);
            if (index >= lines.Count)
            {
                SetAttribute(LogPalette.Resolve(LogTone.Dim));
                AddStr(new string(' ', width));
                continue;
            }

            var line = lines[index];
            DrawLine(line, width);
        }

        return true;
    }

    private void DrawLine(LogDisplayLine line, int width)
    {
        var text = line.Text;
        if (text.Length > width)
        {
            text = text[..width];
        }

        var pad = width - text.Length;
        var baseAttr = LogPalette.Resolve(line.Tone);
        var accents = line.NameAccents;
        if (accents is not { Length: > 0 } || text.Length == 0)
        {
            SetAttribute(baseAttr);
            AddStr(pad > 0 ? text + new string(' ', pad) : text);
            return;
        }

        var cursor = 0;
        while (cursor < text.Length)
        {
            if (!TryFindNextAccent(text, cursor, accents, out var start, out var length, out var name))
            {
                SetAttribute(baseAttr);
                AddStr(text[cursor..]);
                break;
            }

            if (start > cursor)
            {
                SetAttribute(baseAttr);
                AddStr(text[cursor..start]);
            }

            SetAttribute(LogPalette.ResolveName(name));
            AddStr(text.Substring(start, length));
            cursor = start + length;
        }

        if (pad > 0)
        {
            SetAttribute(baseAttr);
            AddStr(new string(' ', pad));
        }
    }

    /// <summary>
    /// Next accent occurrence at or after <paramref name="from"/>. Longer names win on
    /// overlap so e.g. "Archivist" is preferred over "Arch".
    /// </summary>
    private static bool TryFindNextAccent(
        string text,
        int from,
        string[] accents,
        out int start,
        out int length,
        out string name)
    {
        start = text.Length;
        length = 0;
        name = string.Empty;
        var found = false;

        foreach (var accent in accents)
        {
            if (string.IsNullOrEmpty(accent))
            {
                continue;
            }

            var index = text.IndexOf(accent, from, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            if (!found
                || index < start
                || index == start && accent.Length > length)
            {
                start = index;
                length = accent.Length;
                name = accent;
                found = true;
            }
        }

        return found;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _sink.Flushed -= OnFlushed;
        }

        base.Dispose(disposing);
    }

    private bool? ScrollBy(int rows)
    {
        UpdateContentSize();
        if (rows < 0)
        {
            _followTail = false;
        }

        var moved = ScrollVertical(rows) == true;
        if (rows > 0 && IsAtBottom())
        {
            _followTail = true;
        }

        if (moved)
        {
            SetNeedsDraw();
        }

        // Always mark handled so the wheel doesn't fall through to the shell.
        return true;
    }

    private void OnFlushed(object? sender, EventArgs e)
    {
        if (_lastSeenVersion == _sink.Version)
        {
            return;
        }

        _lastSeenVersion = _sink.Version;
        UpdateContentSize();

        if (_followTail || IsAtBottom())
        {
            _followTail = true;
            ScrollToEnd();
        }

        SetNeedsDraw();
    }

    private bool IsAtBottom()
    {
        var lines = _buffer.Count;
        var visible = Math.Max(1, Viewport.Height);
        if (lines <= visible)
        {
            return true;
        }

        return Viewport.Y >= lines - visible;
    }

    private void ScrollToEnd()
    {
        UpdateContentSize();
        var lines = _buffer.Count;
        var visible = Math.Max(1, Viewport.Height);
        var targetY = Math.Max(0, lines - visible);
        var vp = Viewport;
        if (vp.Y == targetY)
        {
            return;
        }

        Viewport = new Rectangle(vp.X, targetY, vp.Width, vp.Height);
    }

    private void UpdateContentSize()
    {
        var height = Math.Max(1, Viewport.Height);
        var width = Math.Max(1, Viewport.Width);
        var contentHeight = Math.Max(height, _buffer.Count);
        var current = GetContentSize();
        if (current.Width != width || current.Height != contentHeight)
        {
            SetContentSize(new Size(width, contentHeight));
        }
    }

    private void EnsureWrapWidth()
    {
        var width = Math.Max(8, Viewport.Width);
        if (width == _appliedWrapWidth)
        {
            return;
        }

        _appliedWrapWidth = width;
        _buffer.SetWrapWidth(width);
        UpdateContentSize();
    }
}
