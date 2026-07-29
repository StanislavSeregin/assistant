namespace Assistant.App;

public static class UsageFormatter
{
    public static string? Format(long? inputTokens)
    {
        if (inputTokens is null)
        {
            return null;
        }

        return $"ctx {FormatK(inputTokens.Value)}";
    }

    private static string FormatK(long tokens)
    {
        var k = tokens / 1024d;
        return tokens % 1024 == 0
            ? $"{k:0}k"
            : $"{k:0.#}k";
    }
}
