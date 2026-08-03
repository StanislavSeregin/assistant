using System;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Application-scoped keys for the Assistant TUI.
/// <list type="bullet">
/// <item>Quit: Ctrl+Q (Esc remains cancel/back in screens).</item>
/// <item>Focus traversal: <see cref="WorkspaceNavigation"/> owns Tab / Shift+Tab.</item>
/// <item>Cursor keys: view-scoped only (scroll, list, caret).</item>
/// </list>
/// </summary>
internal sealed class TuiInputPolicy : IDisposable
{
    private readonly IApplication _app;
    private readonly EventHandler _onKeyBindingsChanged;
    private bool _disposed;

    public TuiInputPolicy(IApplication app)
    {
        _app = app;
        _onKeyBindingsChanged = (_, _) => ClearAppTraversalKeys();
    }

    public void Apply()
    {
        Application.SetDefaultKeyBinding(Command.Quit, Bind.All(Key.Q.WithCtrl));
        Application.RemoveDefaultKeyBinding(Command.NextTabStop);
        Application.RemoveDefaultKeyBinding(Command.PreviousTabStop);
        ClearAppTraversalKeys();
        Application.DefaultKeyBindingsChanged += _onKeyBindingsChanged;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Application.DefaultKeyBindingsChanged -= _onKeyBindingsChanged;
    }

    private void ClearAppTraversalKeys()
    {
        var bindings = _app.Keyboard.KeyBindings;
        bindings.Remove(Key.Tab);
        bindings.Remove(Key.Tab.WithShift);
        bindings.Remove(Key.CursorUp);
        bindings.Remove(Key.CursorDown);
        bindings.Remove(Key.CursorLeft);
        bindings.Remove(Key.CursorRight);
    }
}
