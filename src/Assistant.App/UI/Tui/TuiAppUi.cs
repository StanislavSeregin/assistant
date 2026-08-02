using Assistant.App.UI.Tui.Compose;
using Assistant.App.UI.Tui.Log;
using Assistant.App.UI.Tui.Shell;
using Microsoft.Extensions.Options;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Owns the Terminal.Gui application lifetime. Replaceable if another toolkit is chosen.
/// </summary>
public sealed class TuiAppUi(
    TuiUiScheduler scheduler,
    CoalescingLogSink logSink,
    LogBuffer logBuffer,
    MailComposerHub composerHub,
    IOptions<Settings> settings) : IAppUi
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
        scheduler.Attach(app);
        logSink.Start();

        try
        {
            var logPane = new LogPaneView(logBuffer, logSink);
            var composePane = new ComposePaneView(settings.Value.UserMailSubject);
            composerHub.Attach(composePane);

            var shell = new AssistantShell(logPane, composePane);
            composePane.FocusBody();
            app.Run(shell);
        }
        finally
        {
            logSink.Dispose();
            scheduler.Detach();
        }
    }
}
