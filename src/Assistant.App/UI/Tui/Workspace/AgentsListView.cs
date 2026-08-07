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
    string? ActiveActor,
    string Body);

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

    /// <summary>Column of the braille spinner (after <see cref="LeftPad"/>).</summary>
    private const int GlyphColumn = 1;

    private IReadOnlyList<AgentListRow> _items = [];
    private int _selected;
    private int _spinFrame;
    private bool _spinGlyphOnly;

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
        if (!IsFrontTabPage())
        {
            return;
        }

        // Only the braille column changes — avoid a full-list string rebuild every 80ms.
        _spinGlyphOnly = true;
        var height = Math.Max(1, Viewport.Height);
        SetNeedsDraw(new System.Drawing.Rectangle(GlyphColumn, 0, 1, height));
    }

    public void ResetSpin()
    {
        _spinFrame = 0;
        _spinGlyphOnly = false;
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
            _spinGlyphOnly = false;
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

        if (TryDrawSpinGlyphsOnly())
        {
            return true;
        }

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
                AddStr(PadCache.Spaces(width));
                continue;
            }

            DrawRow(_items[index], selected: index == _selected, width, glyph);
        }

        return true;
    }

    /// <summary>
    /// Spin ticks dirty only the glyph column; rewrite those cells without rebuilding row text.
    /// </summary>
    private bool TryDrawSpinGlyphsOnly()
    {
        if (!_spinGlyphOnly)
        {
            return false;
        }

        _spinGlyphOnly = false;

        var height = Math.Max(1, Viewport.Height);
        var top = Math.Clamp(Viewport.Y, 0, Math.Max(0, _items.Count - 1));
        var glyph = SpinFrames[_spinFrame % SpinFrames.Length];

        for (var row = 0; row < height; row++)
        {
            var index = top + row;
            if (index >= _items.Count)
            {
                continue;
            }

            var item = _items[index];
            var role = index == _selected
                ? (HasFocus ? VisualRole.Focus : VisualRole.Active)
                : VisualRole.Normal;
            var rowAttr = GetAttributeForRole(role);

            if (item.IsBusy)
            {
                SetAttribute(new Attribute(TuiStatusColors.AccentGreen, rowAttr.Background, rowAttr.Style));
                AddStr(GlyphColumn, row, glyph);
            }
            else
            {
                // Framework may have cleared this dirty cell; restore the idle spacer.
                SetAttribute(rowAttr);
                AddStr(GlyphColumn, row, " ");
            }
        }

        return true;
    }

    private void DrawRow(AgentListRow item, bool selected, int width, string glyph)
    {
        var role = selected
            ? (HasFocus ? VisualRole.Focus : VisualRole.Active)
            : VisualRole.Normal;
        var rowAttr = GetAttributeForRole(role);

        if (!item.IsBusy)
        {
            SetAttribute(rowAttr);
            AddFitted(LeftPad, IdlePrefix, item.Body, width);
            return;
        }

        // prefix = LeftPad + glyph + " "  (length 3)
        const int busyPrefixLen = 3;
        var glyphAttr = new Attribute(TuiStatusColors.AccentGreen, rowAttr.Background, rowAttr.Style);
        if (busyPrefixLen >= width)
        {
            // Extremely narrow viewport: paint what fits of pad+glyph(+space).
            SetAttribute(rowAttr);
            AddStr(LeftPad);
            if (width >= 2)
            {
                SetAttribute(glyphAttr);
                AddStr(glyph);
            }

            return;
        }

        SetAttribute(rowAttr);
        AddStr(LeftPad);
        SetAttribute(glyphAttr);
        AddStr(glyph);
        SetAttribute(rowAttr);
        AddStr(" ");
        AddFitted(item.Body, width - busyPrefixLen);
    }

    private void AddFitted(string text, int width)
    {
        if (width <= 0)
        {
            return;
        }

        if (text.Length > width)
        {
            AddStr(text[..width]);
            return;
        }

        AddStr(text);
        if (text.Length < width)
        {
            AddStr(PadCache.Spaces(width - text.Length));
        }
    }

    private void AddFitted(string a, string b, string c, int width)
    {
        if (width <= 0)
        {
            return;
        }

        var used = 0;
        used += AddClipped(a, width - used);
        used += AddClipped(b, width - used);
        used += AddClipped(c, width - used);
        if (used < width)
        {
            AddStr(PadCache.Spaces(width - used));
        }
    }

    private int AddClipped(string text, int remaining)
    {
        if (remaining <= 0 || text.Length == 0)
        {
            return 0;
        }

        if (text.Length <= remaining)
        {
            AddStr(text);
            return text.Length;
        }

        AddStr(text[..remaining]);
        return remaining;
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
