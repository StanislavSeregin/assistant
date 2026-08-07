using System;

namespace Assistant.App.UI.Formatting;

/// <summary>Cached space strings so TUI redraw paths can pad without per-frame allocs.</summary>
internal static class PadCache
{
    private static string[] _spaces = new string[256];

    public static string Spaces(int count)
    {
        if (count <= 0)
        {
            return string.Empty;
        }

        if ((uint)count >= (uint)_spaces.Length)
        {
            Array.Resize(ref _spaces, count + 1);
        }

        return _spaces[count] ??= new string(' ', count);
    }
}
