using System;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Single-overlay navigator for a tab: root stays in the hierarchy (hidden while an
/// overlay is shown); overlays are disposed on replace/pop. Focus is always driven
/// to the deepest TabStop of the top screen so list/input hotkeys keep working after
/// tab switches.
/// </summary>
public sealed class ScreenHostView : View
{
    private View? _root;
    private View? _overlay;

    public ScreenHostView()
    {
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        Width = Dim.Fill();
        Height = Dim.Fill();

        AddCommand(Command.Cancel, () =>
        {
            if (IsRootScreen)
            {
                return false;
            }

            RequestPopToRoot?.Invoke();
            return true;
        });
        // Esc → Cancel is default on View in some contexts; replace to be safe.
        KeyBindings.Remove(Key.Esc);
        KeyBindings.Add(Key.Esc, Command.Cancel);
    }

    /// <summary>Dismiss overlay (reload list, etc.). Wired by the owning tab.</summary>
    public Action? RequestPopToRoot { get; set; }

    public View? Current => _overlay ?? _root;

    public bool IsRootScreen => _overlay is null && _root is not null;

    public void Reset(View root)
    {
        ArgumentNullException.ThrowIfNull(root);

        ClearOverlay();

        if (_root is not null && !ReferenceEquals(_root, root))
        {
            Remove(_root);
            _root.Dispose();
            _root = null;
        }

        _root = root;
        PlaceFull(_root);
        if (_root.SuperView != this)
        {
            Add(_root);
        }

        _root.Visible = true;
        FocusTop();
    }

    public void Push(View screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        if (_root is null)
        {
            throw new InvalidOperationException("Screen host has no root.");
        }

        ClearOverlay();

        _root.Visible = false;
        _overlay = screen;
        PlaceFull(screen);
        Add(screen);
        FocusTop();
    }

    public bool TryPopToRoot()
    {
        if (_overlay is null)
        {
            return false;
        }

        ClearOverlay();
        if (_root is not null)
        {
            _root.Visible = true;
            FocusTop();
        }

        return true;
    }

    /// <summary>Re-enter the top screen after the owning tab becomes selected again.</summary>
    public void FocusCurrent() => FocusTop();

    private void ClearOverlay()
    {
        if (_overlay is null)
        {
            return;
        }

        var dying = _overlay;
        _overlay = null;
        Remove(dying);
        dying.Dispose();
    }

    private void FocusTop()
    {
        var top = Current;
        if (top is null)
        {
            return;
        }

        top.SetFocus();
        if (!top.FocusDeepest(NavigationDirection.Forward, TabBehavior.TabStop))
        {
            top.SetFocus();
        }
    }

    private static void PlaceFull(View screen)
    {
        screen.X = 0;
        screen.Y = 0;
        screen.Width = Dim.Fill();
        screen.Height = Dim.Fill();
    }
}
