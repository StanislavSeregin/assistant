using Assistant.App.Mail;
using Assistant.App.UI.Formatting;
using System;
using System.Collections.Generic;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Color = Terminal.Gui.Drawing.Color;
using Size = System.Drawing.Size;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Inbox list with scheme-driven selection/focus. Only the NEW marker uses a distinct
/// foreground; the rest of the row keeps Normal/Focus/Active attributes.
/// </summary>
internal sealed class InboxListView : View
{
    private const string NewMarker = "NEW ";
    private const string ReadMarker = "    ";

    /// <summary>Dark green stays readable on both normal and focused (gray) row backgrounds.</summary>
    private static readonly Color NewMarkerForeground = Color.Green;

    private IReadOnlyList<InboxItem> _items = [];
    private int _selected;

    public InboxListView()
    {
        CanFocus = true;
        TabStop = TabBehavior.TabStop;
        ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;

        AddCommand(Command.ScrollUp, () => MoveSelection(-1));
        AddCommand(Command.ScrollDown, () => MoveSelection(+1));
        AddCommand(Command.PageUp, () => MoveSelection(-Math.Max(1, Viewport.Height)));
        AddCommand(Command.PageDown, () => MoveSelection(+Math.Max(1, Viewport.Height)));
        AddCommand(Command.Home, () => SelectIndex(0));
        AddCommand(Command.End, () => SelectIndex(_items.Count - 1));
        AddCommand(Command.Accept, () =>
        {
            AcceptSelected?.Invoke();
            return true;
        });

        KeyBindings.Add(Key.CursorUp, Command.ScrollUp);
        KeyBindings.Add(Key.CursorDown, Command.ScrollDown);
        KeyBindings.Add(Key.PageUp, Command.PageUp);
        KeyBindings.Add(Key.PageDown, Command.PageDown);
        KeyBindings.Add(Key.Home, Command.Home);
        KeyBindings.Add(Key.End, Command.End);
    }

    public event Action? AcceptSelected;

    public InboxItem? SelectedItem =>
        _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public void SetItems(IReadOnlyList<InboxItem> items)
    {
        _items = items ?? [];
        if (_items.Count == 0)
        {
            _selected = 0;
            Viewport = Viewport with { Y = 0 };
        }
        else
        {
            _selected = Math.Clamp(_selected, 0, _items.Count - 1);
            EnsureSelectionVisible();
        }

        UpdateContentSize();
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        UpdateContentSize();

        var width = Math.Max(1, Viewport.Width);
        var height = Math.Max(1, Viewport.Height);
        var top = Math.Clamp(Viewport.Y, 0, Math.Max(0, _items.Count - 1));

        for (var row = 0; row < height; row++)
        {
            var index = top + row;
            Move(0, row);

            if (index >= _items.Count)
            {
                SetAttribute(GetAttributeForRole(VisualRole.Normal));
                AddStr(new string(' ', width));
                continue;
            }

            DrawRow(_items[index], selected: index == _selected, width);
        }

        return true;
    }

    private void DrawRow(InboxItem item, bool selected, int width)
    {
        var role = selected
            ? (HasFocus ? VisualRole.Focus : VisualRole.Active)
            : VisualRole.Normal;
        var rowAttr = GetAttributeForRole(role);
        var rest = $"{MailTimestamp.FormatUtc(item.Timestamp)}  {item.From}  {item.Subject}";

        if (item.Status != MailStatus.New)
        {
            SetAttribute(rowAttr);
            AddStr(TextWrapping.Fit(ReadMarker + rest, width));
            return;
        }

        var markerAttr = new Attribute(NewMarkerForeground, rowAttr.Background, rowAttr.Style);
        if (NewMarker.Length >= width)
        {
            SetAttribute(markerAttr);
            AddStr(TextWrapping.Fit(NewMarker, width));
            return;
        }

        SetAttribute(markerAttr);
        AddStr(NewMarker);
        SetAttribute(rowAttr);
        AddStr(TextWrapping.Fit(rest, width - NewMarker.Length));
    }

    private bool MoveSelection(int delta) =>
        _items.Count == 0 || SelectIndex(_selected + delta);

    private bool SelectIndex(int index)
    {
        if (_items.Count == 0)
        {
            return true;
        }

        var next = Math.Clamp(index, 0, _items.Count - 1);
        if (next != _selected)
        {
            _selected = next;
            SetNeedsDraw();
        }

        EnsureSelectionVisible();
        return true;
    }

    private void EnsureSelectionVisible()
    {
        var height = Math.Max(1, Viewport.Height);
        var top = Viewport.Y;
        if (_selected < top)
        {
            top = _selected;
        }
        else if (_selected >= top + height)
        {
            top = _selected - height + 1;
        }

        top = Math.Clamp(top, 0, Math.Max(0, _items.Count - 1));
        if (Viewport.Y != top)
        {
            Viewport = Viewport with { Y = top };
        }

        UpdateContentSize();
    }

    private void UpdateContentSize()
    {
        var width = Math.Max(1, Viewport.Width);
        var contentHeight = Math.Max(Viewport.Height, _items.Count);
        var current = GetContentSize();
        if (current.Width != width || current.Height != contentHeight)
        {
            SetContentSize(new Size(width, contentHeight));
        }
    }
}
