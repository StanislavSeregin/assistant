using System;
using System.Collections.Generic;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Workspace keyboard and page-activation model.
/// <list type="bullet">
/// <item>Arrows stay inside the focused control (scroll, list, caret).</item>
/// <item>Tab / Shift+Tab walk fields on the active screen, then the sibling page.</item>
/// <item>Log is a separate full screen outside the Tab ring; F5 toggles Workspace ↔ Log.</item>
/// <item>Page headers and Tab activation always leave keyboard focus in screen content.</item>
/// </list>
/// </summary>
internal sealed class WorkspaceNavigation : IDisposable
{
    private readonly IApplication _app;
    private readonly WorkspaceTabs _tabs;
    private readonly IWorkspaceTab _agents;
    private readonly IWorkspaceTab _inbox;
    private readonly EventHandler<Key> _onKeyDown;
    private bool _disposed;

    public WorkspaceNavigation(
        IApplication app,
        WorkspaceTabs tabs,
        IWorkspaceTab agents,
        IWorkspaceTab inbox)
    {
        _app = app;
        _tabs = tabs;
        _agents = agents;
        _inbox = inbox;
        _onKeyDown = OnKeyDown;

        _app.Keyboard.KeyDown += _onKeyDown;
        _tabs.HeaderActivated += OnHeaderActivated;
    }

    public void ActivateAgents() => Activate(_agents, FocusEdge.First);

    public void ActivateInbox() => Activate(_inbox, FocusEdge.First);

    public void FocusActiveContent() =>
        Activate(CurrentPane() ?? _agents, FocusEdge.First);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _app.Keyboard.KeyDown -= _onKeyDown;
        _tabs.HeaderActivated -= OnHeaderActivated;
    }

    private void OnKeyDown(object? sender, Key key)
    {
        if (!TryReadTabDirection(key, out var forward))
        {
            return;
        }

        if (CurrentPane() is null && !IsUnderTabs(_app.Navigation?.GetFocused()))
        {
            return;
        }

        Advance(forward);
        key.Handled = true;
    }

    private void OnHeaderActivated(View page)
    {
        if (page is IWorkspaceTab pane)
        {
            Activate(pane, FocusEdge.First);
        }
    }

    private void Advance(bool forward)
    {
        var current = CurrentPane();
        if (current is null)
        {
            return;
        }

        var stops = CollectTabStops(current.CurrentScreen);
        var index = IndexOfFocused(stops, _app.Navigation?.GetFocused());

        if (TryMoveWithinScreen(stops, index, forward))
        {
            return;
        }

        Activate(Sibling(current), forward ? FocusEdge.First : FocusEdge.Last);
    }

    /// <summary>
    /// Moves among fields on the current screen. Returns false when Tab should leave
    /// the screen (no/single field, or past the last/first field).
    /// </summary>
    private static bool TryMoveWithinScreen(IReadOnlyList<View> stops, int index, bool forward)
    {
        if (index < 0)
        {
            if (stops.Count <= 1)
            {
                return false;
            }

            stops[forward ? 0 : stops.Count - 1].SetFocus();
            return true;
        }

        if (forward)
        {
            if (index >= stops.Count - 1)
            {
                return false;
            }

            stops[index + 1].SetFocus();
            return true;
        }

        if (index <= 0)
        {
            return false;
        }

        stops[index - 1].SetFocus();
        return true;
    }

    private void Activate(IWorkspaceTab pane, FocusEdge edge)
    {
        var page = (View)pane;
        if (!ReferenceEquals(_tabs.Value, page))
        {
            _tabs.SelectPage(page, () => FocusPaneContent(pane, edge));
            return;
        }

        FocusPaneContent(pane, edge);
    }

    private static void FocusPaneContent(IWorkspaceTab pane, FocusEdge edge)
    {
        var stops = CollectTabStops(pane.CurrentScreen);
        if (stops.Count > 0)
        {
            var target = edge == FocusEdge.First ? stops[0] : stops[^1];
            if (target.SetFocus())
            {
                return;
            }
        }

        pane.FocusContent();
        if (pane.CurrentScreen is { HasFocus: false } screen)
        {
            screen.SetFocus();
        }
    }

    private IWorkspaceTab? CurrentPane() =>
        _tabs.Value as IWorkspaceTab
        ?? FindOwningPane(_app.Navigation?.GetFocused());

    private IWorkspaceTab Sibling(IWorkspaceTab current) =>
        ReferenceEquals(current, _agents) ? _inbox : _agents;

    private static IWorkspaceTab? FindOwningPane(View? start)
    {
        for (var view = start; view is not null; view = view.SuperView)
        {
            if (view is IWorkspaceTab pane)
            {
                return pane;
            }
        }

        return null;
    }

    private bool IsUnderTabs(View? start)
    {
        for (var view = start; view is not null; view = view.SuperView)
        {
            if (ReferenceEquals(view, _tabs))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryReadTabDirection(Key key, out bool forward)
    {
        if (key == Key.Tab)
        {
            forward = true;
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            forward = false;
            return true;
        }

        if (key.KeyCode == Key.Tab.KeyCode)
        {
            forward = !key.IsShift;
            return true;
        }

        forward = true;
        return false;
    }

    private static List<View> CollectTabStops(View? root)
    {
        var stops = new List<View>();
        if (root is not null)
        {
            CollectTabStops(root, stops);
        }

        return stops;
    }

    private static void CollectTabStops(View view, List<View> into)
    {
        foreach (var child in view.SubViews)
        {
            if (!child.Enabled)
            {
                continue;
            }

            // Include hidden stops: overlapped inactive pages hide content until selected.
            if (child.CanFocus && child.TabStop == TabBehavior.TabStop)
            {
                into.Add(child);
            }

            CollectTabStops(child, into);
        }
    }

    private static int IndexOfFocused(IReadOnlyList<View> stops, View? focused)
    {
        if (focused is null)
        {
            return -1;
        }

        for (var i = 0; i < stops.Count; i++)
        {
            if (ReferenceEquals(stops[i], focused)
                || ApplicationNavigation.IsInHierarchy(stops[i], focused))
            {
                return i;
            }
        }

        return -1;
    }

    private enum FocusEdge
    {
        First,
        Last
    }
}
