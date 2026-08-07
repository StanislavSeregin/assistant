using Terminal.Gui.Drawing;
using Color = Terminal.Gui.Drawing.Color;

namespace Assistant.App.UI.Tui;

/// <summary>Shared status accents across list rows (inbox NEW, agent busy glyph, …).</summary>
internal static class TuiStatusColors
{
    /// <summary>Readable on both normal and focused (gray) row backgrounds.</summary>
    public static Color AccentGreen { get; } = Color.Green;
}
