using Assistant.App.UI.Tui.Log;
using Assistant.App.UI.Tui.Workspace;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Root tiled shell: log on top, Inbox/Agents workspace on bottom.
/// </summary>
public sealed class AssistantShell : Window
{
    public AssistantShell(
        LogPaneView logPane,
        InboxTabView inboxTab,
        AgentsTabView agentsTab)
    {
        Title = "Assistant";
        BorderStyle = LineStyle.None;

        var logFrame = CreateTileFrame(
            caption: "Log",
            x: 0,
            y: 0,
            width: Dim.Fill(),
            height: Dim.Percent(40),
            arrangement: ViewArrangement.BottomResizable);
        logPane.X = 0;
        logPane.Y = 1;
        logPane.Width = Dim.Fill();
        logPane.Height = Dim.Fill();
        logPane.CanFocus = true;
        logPane.TabStop = TabBehavior.TabStop;
        logFrame.CommandsToBubbleUp = [Command.ScrollUp, Command.ScrollDown, Command.PageUp, Command.PageDown];
        logFrame.Add(logPane);

        var workspaceFrame = CreateTileFrame(
            caption: null,
            x: 0,
            y: Pos.Bottom(logFrame),
            width: Dim.Fill(),
            height: Dim.Fill(1),
            arrangement: ViewArrangement.Fixed);

        var tabs = new Tabs
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
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
        tabs.Add(agentsTab, inboxTab);
        tabs.Value = agentsTab;
        workspaceFrame.Add(tabs);

        void ShowInboxAfterSend()
        {
            tabs.Value = inboxTab;
            inboxTab.ActivateList();
        }

        inboxTab.OutgoingMailSent += ShowInboxAfterSend;
        agentsTab.OutgoingMailSent += ShowInboxAfterSend;

        void Quit() => App?.RequestStop();

        // Window default Quit throws when not running as a modal Runnable.
        // Application Quit is rebound to Ctrl+Q in TuiAppUi; keep a safe handler here.
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

        var focusLog = new Shortcut
        {
            Title = "Log",
            Key = Key.F6,
            BindKeyToApplication = true
        };
        focusLog.Activated += (_, _) => logPane.SetFocus();

        var focusWorkspace = new Shortcut
        {
            Title = "Workspace",
            Key = Key.F7,
            BindKeyToApplication = true
        };
        focusWorkspace.Activated += (_, _) => tabs.SetFocus();

        statusBar.Add(quit, focusLog, focusWorkspace);

        Add(logFrame, workspaceFrame, statusBar);

        LogFrame = logFrame;
        WorkspaceFrame = workspaceFrame;
        LogPane = logPane;
        Tabs = tabs;

        logPane.HasFocusChanged += (_, _) =>
            SetTileFocusChrome(logFrame, logPane.HasFocus);
        tabs.HasFocusChanged += (_, _) =>
            SetTileFocusChrome(workspaceFrame, tabs.HasFocus);
    }

    public FrameView LogFrame { get; }
    public FrameView WorkspaceFrame { get; }
    public LogPaneView LogPane { get; }
    public Tabs Tabs { get; }

    private static FrameView CreateTileFrame(
        string? caption,
        Pos x,
        Pos y,
        Dim width,
        Dim height,
        ViewArrangement arrangement)
    {
        var frame = new FrameView
        {
            Title = string.Empty,
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Arrangement = arrangement,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
            TabStop = TabBehavior.TabGroup
        };
        frame.Border.Settings = BorderSettings.Default;

        if (!string.IsNullOrEmpty(caption))
        {
            var header = new Label
            {
                Text = caption,
                X = 1,
                Y = 0,
                Width = Dim.Fill(1),
                CanFocus = false,
                TabStop = TabBehavior.NoStop
            };
            frame.Add(header);
        }

        return frame;
    }

    private static void SetTileFocusChrome(FrameView frame, bool focused)
    {
        frame.BorderStyle = focused ? LineStyle.Heavy : LineStyle.Rounded;
        frame.Title = string.Empty;
        frame.Border.Settings = BorderSettings.Default;
    }
}
