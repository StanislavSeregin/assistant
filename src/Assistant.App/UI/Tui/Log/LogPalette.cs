using Assistant.App.UI.Abstractions;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Assistant.App.UI.Tui.Log;

/// <summary>
/// Maps semantic <see cref="LogTone"/> to Terminal.Gui attributes.
/// Override or replace to theme without touching the presenter.
/// </summary>
public static class LogPalette
{
    private static readonly Color Bg = Color.Black;

    public static Attribute Resolve(LogTone tone) => tone switch
    {
        LogTone.Grey => new Attribute(Color.DarkGray, Bg),
        LogTone.GreyItalic => new Attribute(Color.DarkGray, Bg, TextStyle.Italic),
        LogTone.Yellow => new Attribute(Color.BrightYellow, Bg),
        LogTone.Red => new Attribute(Color.BrightRed, Bg),
        LogTone.Blue => new Attribute(Color.BrightBlue, Bg),
        LogTone.Magenta => new Attribute(Color.BrightMagenta, Bg),
        LogTone.Cyan => new Attribute(Color.BrightCyan, Bg),
        LogTone.BoldGreen => new Attribute(Color.BrightGreen, Bg, TextStyle.Bold),
        LogTone.BoldWhite => new Attribute(Color.White, Bg, TextStyle.Bold),
        LogTone.Dim => new Attribute(Color.DarkGray, Bg, TextStyle.Faint),
        _ => new Attribute(Color.Gray, Bg)
    };
}
