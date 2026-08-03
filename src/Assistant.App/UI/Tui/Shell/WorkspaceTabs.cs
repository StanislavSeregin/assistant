using System;
using Terminal.Gui.Input;
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
