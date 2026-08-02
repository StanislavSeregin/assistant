using System.Globalization;

namespace Assistant.App.UI.Console;

public static class UsageFormatter
{
    public static string Format(long? inputTokens)
    {
        if (inputTokens is not long tokens)
        {
            return string.Empty;
        }

        return $"in {FormatCompactNumber(tokens)}";
    }

    public static string FormatCompactNumber(long value)
    {
        if (value < 1000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        return (value / 1000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
    }
}
