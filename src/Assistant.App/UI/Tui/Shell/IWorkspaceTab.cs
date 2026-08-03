using Terminal.Gui.ViewBase;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Workspace page that owns a screen stack (list + overlays).
/// </summary>
internal interface IWorkspaceTab
{
    View? CurrentScreen { get; }

    void FocusContent();
}
