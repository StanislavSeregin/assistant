using System;
using Terminal.Gui.Input;
using Terminal.Gui.Text;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Agents/Inbox host. Pages are screen stacks; headers select a page without taking
/// keyboard focus; arrow keys never change pages.
/// </summary>
internal sealed class WorkspaceTabs : Tabs
{
    private int _suppressFocusSync;

    public WorkspaceTabs()
    {
        // Content owns directional keys; do not use them for page switching.
        AddCommand(Command.Up, static () => true);
        AddCommand(Command.Down, static () => true);
        AddCommand(Command.Left, static () => true);
        AddCommand(Command.Right, static () => true);
    }

    /// <summary>User clicked a page header.</summary>
    public event Action<View>? HeaderActivated;

    /// <summary>
    /// Sets the tab-strip label for <paramref name="page"/>.
    /// Terminal.Gui Tabs render <see cref="BorderView.TitleView"/>, which does not
    /// reliably follow <see cref="View.Title"/> — keep both in sync and resize
    /// <see cref="BorderView.TabLength"/> so longer titles (e.g. inbox badges) fit.
    /// </summary>
    public static void SetPageTitle(View page, string title)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(title);

        page.Title = title;

        if (page.Border?.View is BorderView border)
        {
            // Title columns + the two border cells of the tab cap (toolkit convention).
            border.TabLength = title.GetColumns() + 2;

            if (border.TitleView is { } titleView)
            {
                titleView.Text = title;
                titleView.SetNeedsDraw();
            }
        }

        page.SetNeedsLayout();
        if (page.SuperView is { } host)
        {
            host.SetNeedsLayout();
            host.SetNeedsDraw();
        }
    }

    /// <summary>
    /// Selects <paramref name="page"/> and runs <paramref name="focusContent"/> after
    /// the page is the frontmost SubView. Suppresses Tabs' focus→Value sync so a
    /// failed intermediate SetFocus cannot revert the selection.
    /// </summary>
    public void SelectPage(View page, Action focusContent)
    {
        _suppressFocusSync++;
        try
        {
            Value = page;
            focusContent();
            if (!page.HasFocus)
            {
                page.SetFocus();
            }

            focusContent();
        }
        finally
        {
            _suppressFocusSync--;
        }
    }

    protected override void OnSubViewAdded(View view)
    {
        base.OnSubViewAdded(view);
        view.TabStop = TabBehavior.TabGroup;

        // Ensure the strip label matches Title at attach time (same contract as SetPageTitle).
        SetPageTitle(view, view.Title ?? string.Empty);

        if (view.Border?.View is not BorderView { TitleView: { } title } border)
        {
            return;
        }

        border.CommandsToBubbleUp = [];
        title.CanFocus = false;
        title.MouseBindings.Clear();
        title.MouseEvent += (_, mouse) =>
        {
            if (!mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked))
            {
                return;
            }

            HeaderActivated?.Invoke(view);
            mouse.Handled = true;
        };
    }

    protected override void OnFocusedChanged(View? previousFocused, View? focused)
    {
        if (_suppressFocusSync > 0)
        {
            return;
        }

        base.OnFocusedChanged(previousFocused, focused);
    }
}
