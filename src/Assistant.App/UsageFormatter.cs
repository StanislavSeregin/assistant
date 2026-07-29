namespace Assistant.App;

public static class UsageFormatter
{
    public static string? FormatFill(long? inputTokens, long? contextWindowTokens)
    {
        if (inputTokens is null)
        {
            return null;
        }

        if (contextWindowTokens is > 0)
        {
            return $"ctx {FormatK(inputTokens.Value)}/{FormatK(contextWindowTokens.Value)}";
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
