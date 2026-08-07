using Assistant.App.UI.Formatting;
using System;
using System.Collections.Generic;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;
using Size = System.Drawing.Size;

namespace Assistant.App.UI.Tui.Workspace;

internal sealed record AgentListRow(
    string Name,
    bool IsBusy,
    string? ActiveActor);

/// <summary>
/// Agents list with scheme-driven selection. Busy glyph is green (same accent as inbox NEW);
/// the rest of the row keeps Normal/Focus/Active attributes.
/// Layout: <c>{glyph?} [Name]{ · actor?}</c>
/// </summary>
internal sealed class AgentsListView : View
{
    private static readonly string[] SpinFrames =
    [
        "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"
    ];

    /// <summary>Leading gutter so the glyph is not flush with the frame.</summary>
    private const string LeftPad = " ";

    /// <summary>Idle spacer matches <c>glyph + space</c> so the bracket column stays put.</summary>
    private const string IdlePrefix = "  ";

    private IReadOnlyList<AgentListRow> _items = [];
    private int _selected;
    private int _spinFrame;

    public AgentsListView()
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

    public AgentListRow? SelectedItem =>
        _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

    public void SetItems(IReadOnlyList<AgentListRow> items)
    {
        var selectedName = SelectedItem?.Name;
        _items = items ?? [];
        if (_items.Count == 0)
        {
            _selected = 0;
            Viewport = Viewport with { Y = 0 };
        }
        else if (selectedName is not null)
        {
            var index = IndexOfName(selectedName);
            _selected = index >= 0 ? index : Math.Clamp(_selected, 0, _items.Count - 1);
            EnsureSelectionVisible();
        }
        else
        {
            _selected = Math.Clamp(_selected, 0, _items.Count - 1);
            EnsureSelectionVisible();
        }

        UpdateContentSize();
        RequestDraw();
    }

    public void AdvanceSpin()
    {
        _spinFrame++;
        // Skip invalidate while another tab is front — redrawing this page steals
        // Tabs header chrome even though Inbox content is showing.
        RequestDraw();
    }

    public void ResetSpin()
    {
        _spinFrame = 0;
        RequestDraw();
    }

    /// <summary>
    /// Inactive Tabs pages stay in the tree; SetNeedsDraw on them makes that page's
    /// header look selected. Only invalidate when our page is the Tabs value.
    /// </summary>
    private void RequestDraw()
    {
        if (IsFrontTabPage())
        {
            SetNeedsDraw();
        }
    }

    private bool IsFrontTabPage()
    {
        for (View? view = this; view is not null; view = view.SuperView)
        {
            if (view.SuperView is Tabs tabs)
            {
                return ReferenceEquals(tabs.Value, view);
            }
        }

        return Visible;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        UpdateContentSize();

        var width = Math.Max(1, Viewport.Width);
        var height = Math.Max(1, Viewport.Height);
        var top = Math.Clamp(Viewport.Y, 0, Math.Max(0, _items.Count - 1));
        var glyph = SpinFrames[_spinFrame % SpinFrames.Length];

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

            DrawRow(_items[index], selected: index == _selected, width, glyph);
        }

        return true;
    }

    private void DrawRow(AgentListRow item, bool selected, int width, string glyph)
    {
        var role = selected
            ? (HasFocus ? VisualRole.Focus : VisualRole.Active)
            : VisualRole.Normal;
        var rowAttr = GetAttributeForRole(role);

        var body = item.ActiveActor is { } actor
            ? $"[{item.Name}] · {actor}"
            : $"[{item.Name}]";

        if (!item.IsBusy)
        {
            SetAttribute(rowAttr);
            AddStr(TextWrapping.Fit(LeftPad + IdlePrefix + body, width));
            return;
        }

        var prefix = LeftPad + glyph + " ";
        var glyphAttr = new Attribute(TuiStatusColors.AccentGreen, rowAttr.Background, rowAttr.Style);
        if (prefix.Length >= width)
        {
            SetAttribute(glyphAttr);
            AddStr(TextWrapping.Fit(prefix, width));
            return;
        }

        SetAttribute(rowAttr);
        AddStr(LeftPad);
        SetAttribute(glyphAttr);
        AddStr(glyph + " ");
        SetAttribute(rowAttr);
        AddStr(TextWrapping.Fit(body, width - prefix.Length));
    }

    private int IndexOfName(string name)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            if (string.Equals(_items[i].Name, name, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
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
            RequestDraw();
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
        var contentHeight = Math.Max(1, Math.Max(Viewport.Height, _items.Count));
        var current = GetContentSize();
        if (current.Width != width || current.Height != contentHeight)
        {
            SetContentSize(new Size(width, contentHeight));
        }
    }
}
