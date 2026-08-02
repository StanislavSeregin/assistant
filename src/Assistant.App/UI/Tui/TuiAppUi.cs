using Assistant.App.UI.Abstractions;
using Assistant.App.UI.Tui.Log;
using Assistant.App.UI.Tui.Shell;
using Assistant.App.UI.Tui.Workspace;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Input;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Owns the Terminal.Gui application lifetime. Replaceable if another toolkit is chosen.
/// </summary>
public sealed class TuiAppUi(
    TuiUiScheduler scheduler,
    CoalescingLogSink logSink,
    LogBuffer logBuffer,
    IUserWorkspace workspace,
    IUiScheduler uiScheduler) : IAppUi
{
    private bool _initialized;

    public void Initialize() => _initialized = true;

    public void Run()
    {
        if (!_initialized)
        {
            Initialize();
        }

        ConfigurationManager.Enable(ConfigLocations.All);

        using var app = Application.Create();
        app.Init();
        // Terminal.Gui defaults Quit to Esc at the application level (RequestStop).
        // Keep Quit on Ctrl+Q only — Esc is used for cancel/back in nested screens.
        Application.SetDefaultKeyBinding(Command.Quit, Bind.All(Key.Q.WithCtrl));
        scheduler.Attach(app);
        logSink.Start();

        try
        {
            var logPane = new LogPaneView(logBuffer, logSink);
            var inboxTab = new InboxTabView(workspace, uiScheduler);
            var agentsTab = new AgentsTabView(workspace, uiScheduler);
            var shell = new AssistantShell(logPane, inboxTab, agentsTab);
            shell.Tabs.SetFocus();
            app.Run(shell);
        }
        finally
        {
            logSink.Dispose();
            scheduler.Detach();
        }
    }
}
