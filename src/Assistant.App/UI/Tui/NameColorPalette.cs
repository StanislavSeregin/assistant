using Assistant.App.UI.Formatting;
using Terminal.Gui.Drawing;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Stable per-name foreground colors for agent/node accents across the TUI.
/// Picks a dark-bg or light-bg palette so names stay readable on both normal
/// rows (dark / terminal default) and Focus/Active selection (white / gray).
/// </summary>
public static class NameColorPalette
{
    /// <summary>Bright hues for dark backgrounds (no near-black).</summary>
    private static readonly Color[] OnDark =
    [
        Color.BrightCyan,
        Color.BrightMagenta,
        Color.BrightYellow,
        Color.BrightGreen,
        Color.BrightBlue,
        Color.White,
        Color.Cyan,
        Color.Magenta,
        Color.Yellow
    ];

    /// <summary>Deep hues for light selection backgrounds (no near-white).</summary>
    private static readonly Color[] OnLight =
    [
        new Color(0x00, 0x5A, 0x9E, 0xFF), // deep azure  ↔ BrightCyan
        new Color(0x9B, 0x00, 0x9B, 0xFF), // deep magenta ↔ BrightMagenta
        new Color(0x8B, 0x5A, 0x00, 0xFF), // dark gold   ↔ BrightYellow
        new Color(0x00, 0x7A, 0x3D, 0xFF), // deep green  ↔ BrightGreen
        new Color(0x00, 0x33, 0x99, 0xFF), // deep blue   ↔ BrightBlue
        new Color(0x1A, 0x1A, 0x1A, 0xFF), // near-black  ↔ White
        new Color(0x00, 0x7A, 0x7A, 0xFF), // teal        ↔ Cyan
        new Color(0x7A, 0x00, 0x5A, 0xFF), // purple      ↔ Magenta
        new Color(0x9A, 0x5A, 0x00, 0xFF) // amber        ↔ Yellow
    ];

    public static Color Foreground(string name, Color background)
    {
        var paintBg = OpaqueBackground(background);
        var index = NameColorIndex.Of(name, OnDark.Length);
        return IsLight(paintBg) ? OnLight[index] : OnDark[index];
    }

    public static Attribute Resolve(string name, Color background, TextStyle style = TextStyle.Bold)
    {
        // Color.None is the scheme sentinel for “terminal default”. Drivers may map it
        // to white; paint an explicit black cell so bright names stay visible.
        var paintBg = OpaqueBackground(background);
        return new(Foreground(name, paintBg), paintBg, style);
    }

    private static Color OpaqueBackground(Color background) =>
        background == Color.None ? Color.Black : background;

    private static bool IsLight(Color background)
    {
        // Rec. 709 relative luminance; Focus is white, Active is ~#CCC.
        var luminance = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) / 255.0;
        return luminance > 0.55;
    }
}
