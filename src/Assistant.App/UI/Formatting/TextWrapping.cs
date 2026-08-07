using System.Collections.Generic;

namespace Assistant.App.UI.Formatting;

/// <summary>Soft-wrap and column-fit helpers for TUI text.</summary>
internal static class TextWrapping
{
    public static IEnumerable<string> Wrap(string text, int width)
    {
        if (width <= 0)
        {
            yield return text;
            yield break;
        }

        if (string.IsNullOrEmpty(text))
        {
            yield return string.Empty;
            yield break;
        }

        var remaining = text;
        while (remaining.Length > width)
        {
            var breakAt = remaining.LastIndexOf(' ', width);
            if (breakAt <= 0)
            {
                breakAt = width;
            }

            yield return remaining[..breakAt];
            remaining = remaining[breakAt..].TrimStart();
        }

        yield return remaining;
    }

    /// <summary>Trim or right-pad <paramref name="text"/> to exactly <paramref name="width"/> columns.</summary>
    public static string Fit(string text, int width)
    {
        if (width <= 0)
        {
            return string.Empty;
        }

        if (text.Length > width)
        {
            return text[..width];
        }

        return text.Length < width
            ? string.Concat(text, PadCache.Spaces(width - text.Length))
            : text;
    }
}

