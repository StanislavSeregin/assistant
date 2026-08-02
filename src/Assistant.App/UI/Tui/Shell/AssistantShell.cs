using Assistant.App.UI.Tui.Compose;
using Assistant.App.UI.Tui.Log;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Assistant.App.UI.Tui.Shell;

/// <summary>
/// Root tiled shell: log on top, compose on bottom. Panes are replaceable SubViews.
/// </summary>
public sealed class AssistantShell : Window
{
    public AssistantShell(
        LogPaneView logPane,
        ComposePaneView composePane)
    {
        Title = "Assistant";
        BorderStyle = LineStyle.None;

        var logFrame = CreateTileFrame(
            "Log",
            x: 0,
            y: 0,
            width: Dim.Fill(),
            height: Dim.Percent(65),
            arrangement: ViewArrangement.BottomResizable);
        logPane.X = 0;
        logPane.Y = 1; // below in-pane header label
        logPane.Width = Dim.Fill();
        logPane.Height = Dim.Fill();
        logPane.CanFocus = true;
        logPane.TabStop = TabBehavior.TabStop;
        logFrame.CommandsToBubbleUp = [Command.ScrollUp, Command.ScrollDown, Command.PageUp, Command.PageDown];
        logFrame.Add(logPane);

        var composeFrame = CreateTileFrame(
            "Compose",
            x: 0,
            y: Pos.Bottom(logFrame),
            width: Dim.Fill(),
            height: Dim.Fill(1),
            arrangement: ViewArrangement.Fixed);
        composePane.X = 0;
        composePane.Y = 1;
        composePane.Width = Dim.Fill();
        composePane.Height = Dim.Fill();
        composeFrame.Add(composePane);

        var statusBar = new StatusBar();
        var quit = new Shortcut
        {
            Title = "Quit",
            Key = Key.Q.WithCtrl,
            BindKeyToApplication = true
        };
        quit.Activated += (_, _) => App?.RequestStop();

        var focusLog = new Shortcut
        {
            Title = "Log",
            Key = Key.F6,
            BindKeyToApplication = true
        };
        focusLog.Activated += (_, _) => logPane.SetFocus();

        var focusCompose = new Shortcut
        {
            Title = "Compose",
            Key = Key.F7,
            BindKeyToApplication = true
        };
        focusCompose.Activated += (_, _) => composePane.FocusBody();

        var focusSubject = new Shortcut
        {
            Title = "Subject",
            Key = Key.F8,
            BindKeyToApplication = true
        };
        focusSubject.Activated += (_, _) => composePane.FocusSubject();

        statusBar.Add(quit, focusLog, focusCompose, focusSubject);

        Add(logFrame, composeFrame, statusBar);

        LogFrame = logFrame;
        ComposeFrame = composeFrame;
        LogPane = logPane;
        ComposePane = composePane;

        logPane.HasFocusChanged += (_, _) =>
            SetTileFocusChrome(logFrame, logPane.HasFocus);
        composePane.HasFocusChanged += (_, _) =>
            SetTileFocusChrome(composeFrame, composePane.HasFocus);
    }

    public FrameView LogFrame { get; }
    public FrameView ComposeFrame { get; }
    public LogPaneView LogPane { get; }
    public ComposePaneView ComposePane { get; }

    /// <summary>
    /// Border without inline Title — avoids Terminal.Gui's <c>┤Title├</c> tee glyphs.
    /// Caption is a plain Label inside the pane.
    /// </summary>
    private static FrameView CreateTileFrame(
        string caption,
        Pos x,
        Pos y,
        Dim width,
        Dim height,
        ViewArrangement arrangement)
    {
        var frame = new FrameView
        {
            // Empty title: with Title enabled, LineCanvas still emits ┤ ├ around text.
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
        // BorderStyle enables Title; strip it so the top edge stays a plain line.
        frame.Border.Settings = BorderSettings.Default;

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
        return frame;
    }

    private static void SetTileFocusChrome(FrameView frame, bool focused)
    {
        frame.BorderStyle = focused ? LineStyle.Heavy : LineStyle.Rounded;
        // BorderStyle re-applies Title; keep the clean edge.
        frame.Title = string.Empty;
        frame.Border.Settings = BorderSettings.Default;
    }
}
