using System;

namespace Assistant.App.UI.Formatting;

/// <summary>
/// Stable name → palette slot. FNV-1a so the same agent always gets the same hue
/// across runs (unlike <see cref="string.GetHashCode()"/>).
/// </summary>
internal static class NameColorIndex
{
    public static int Of(string name, int paletteSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(paletteSize, 1);

        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in name)
            {
                hash ^= c;
                hash *= 16777619;
            }

            return (int)(hash % (uint)paletteSize);
        }
    }
}
