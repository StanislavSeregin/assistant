using Assistant.App.UI.Tui.Log;
using Assistant.App.UI.Tui.Workspace;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Root shell: exclusive full-screen modes for Workspace (default) and Log.
/// </summary>
public sealed class AssistantShell : Window
{
    private readonly WorkspaceNavigation _navigation;
    private readonly FrameView _logFrame;
    private readonly LogPaneView _logPane;
    private readonly WorkspaceTabs _tabs;

    public AssistantShell(
        IApplication app,
        LogPaneView logPane,
        InboxTabView inboxTab,
        AgentsTabView agentsTab)
    {
        Title = "Assistant";
        BorderStyle = LineStyle.None;

        _logFrame = CreateLogFrame();
        _logPane = logPane;
        logPane.X = 0;
        logPane.Y = 1;
        logPane.Width = Dim.Fill();
        logPane.Height = Dim.Fill();
        logPane.CanFocus = true;
        logPane.TabStop = TabBehavior.NoStop;
        _logFrame.CommandsToBubbleUp = [Command.ScrollUp, Command.ScrollDown, Command.PageUp, Command.PageDown];
        _logFrame.Add(logPane);
        _logFrame.Visible = false;

        // No outer frame: Tabs already draw their own border around Agents/Inbox.
        _tabs = new WorkspaceTabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            CanFocus = true,
            TabStop = TabBehavior.TabGroup
        };
        inboxTab.X = 0;
        inboxTab.Y = 0;
        inboxTab.Width = Dim.Fill();
        inboxTab.Height = Dim.Fill();
        agentsTab.X = 0;
        agentsTab.Y = 0;
        agentsTab.Width = Dim.Fill();
        agentsTab.Height = Dim.Fill();
        _tabs.Add(agentsTab, inboxTab);
        _tabs.Value = agentsTab;

        var navigation = new WorkspaceNavigation(app, _tabs, agentsTab, inboxTab);
        _navigation = navigation;

        void ShowInboxAfterSend()
        {
            inboxTab.ActivateList();
            navigation.ActivateInbox();
            ShowWorkspace();
        }

        inboxTab.OutgoingMailSent += ShowInboxAfterSend;
        agentsTab.OutgoingMailSent += ShowInboxAfterSend;

        void Quit() => App?.RequestStop();

        // Window default Quit throws when not running as a modal Runnable.
        AddCommand(Command.Quit, () =>
        {
            Quit();
            return true;
        });
        KeyBindings.Remove(Key.Esc);

        var statusBar = new StatusBar();
        var quit = new Shortcut
        {
            Title = "Quit",
            Key = Key.Q.WithCtrl,
            BindKeyToApplication = true
        };
        quit.Activated += (_, _) => Quit();

        var showWorkspace = new Shortcut
        {
            Title = "Workspace",
            Key = Key.F5,
            BindKeyToApplication = true
        };
        showWorkspace.Activated += (_, _) => ShowWorkspace();

        var showLog = new Shortcut
        {
            Title = "Log",
            Key = Key.F6,
            BindKeyToApplication = true
        };
        showLog.Activated += (_, _) => ShowLog();

        statusBar.Add(quit, showWorkspace, showLog);

        Add(_logFrame, _tabs, statusBar);

        LogFrame = _logFrame;
        LogPane = logPane;
        Tabs = _tabs;
    }

    /// <summary>Startup default: Workspace screen on Agents page with content focused.</summary>
    public void ShowAgents()
    {
        ShowWorkspace();
        _navigation.ActivateAgents();
    }

    public void ShowLog()
    {
        _tabs.Visible = false;
        _logFrame.Visible = true;
        _logPane.SetFocus();
    }

    public void ShowWorkspace()
    {
        _logFrame.Visible = false;
        _tabs.Visible = true;
        _tabs.SetFocus();
        _navigation.FocusActiveContent();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigation.Dispose();
        }

        base.Dispose(disposing);
    }

    public FrameView LogFrame { get; }
    public LogPaneView LogPane { get; }
    public Tabs Tabs { get; }

    private static FrameView CreateLogFrame()
    {
        var frame = new FrameView
        {
            Title = string.Empty,
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Arrangement = ViewArrangement.Fixed,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
            TabStop = TabBehavior.TabGroup
        };
        frame.Border.Settings = BorderSettings.Default;

        var header = new Label
        {
            Text = "Log",
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            CanFocus = false,
            TabStop = TabBehavior.NoStop
        };
        frame.Add(header);

        return frame;
    }
}
