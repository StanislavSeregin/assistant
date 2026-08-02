using System;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Workspace;

/// <summary>
/// Hosts a navigation stack: root list stays alive; overlays are disposed on pop.
/// </summary>
public sealed class ScreenHostView : View
{
    private View? _root;
    private View? _current;

    public ScreenHostView()
    {
        CanFocus = true;
        TabStop = TabBehavior.TabGroup;
        Width = Dim.Fill();
        Height = Dim.Fill();
    }

    public View? Current => _current;

    public bool IsRootScreen => _current is not null && ReferenceEquals(_current, _root);

    public void Reset(View root)
    {
        DisposeOverlay();
        if (_root is not null && !ReferenceEquals(_root, root))
        {
            Remove(_root);
            _root.Dispose();
        }

        _root = root;
        _current = null;
        ShowRoot();
    }

    public void Push(View screen)
    {
        if (_root is null)
        {
            throw new InvalidOperationException("Screen host has no root.");
        }

        if (ReferenceEquals(_current, _root))
        {
            Remove(_root);
            _current = null;
        }
        else
        {
            DisposeOverlay();
        }

        Attach(screen);
    }

    public bool TryPopToRoot()
    {
        if (_root is null || IsRootScreen)
        {
            return false;
        }

        DisposeOverlay();
        ShowRoot();
        return true;
    }

    private void ShowRoot()
    {
        if (_root is null)
        {
            return;
        }

        Attach(_root);
    }

    private void Attach(View screen)
    {
        screen.X = 0;
        screen.Y = 0;
        screen.Width = Dim.Fill();
        screen.Height = Dim.Fill();
        _current = screen;
        if (screen.SuperView != this)
        {
            Add(screen);
        }

        screen.SetFocus();
    }

    private void DisposeOverlay()
    {
        if (_current is null || ReferenceEquals(_current, _root))
        {
            _current = null;
            return;
        }

        Remove(_current);
        _current.Dispose();
        _current = null;
    }
}
