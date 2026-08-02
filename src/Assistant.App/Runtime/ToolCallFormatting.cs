using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Assistant.App.Runtime;

internal static class ToolCallFormatting
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string? FormatResult(object? result) =>
        result switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text,
            _ => result.ToString()
        };

    public static string? FormatArgument(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => JsonSerializer.Serialize(element, JsonOptions),
            _ => JsonSerializer.Serialize(value, JsonOptions)
        };

    public static IReadOnlyDictionary<string, string?>? FormatArguments(
        IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        if (arguments is null)
        {
            return null;
        }

        var formatted = arguments.ToDictionary(
            argument => argument.Key,
            argument => FormatArgument(argument.Value),
            StringComparer.Ordinal);
        return formatted.Count == 0 ? null : formatted;
    }

    public static void CollectToolCalls(
        AgentResponseUpdate update,
        Dictionary<string, FunctionCallContent> toolCalls)
    {
        foreach (var call in update.Contents.OfType<FunctionCallContent>())
        {
            var key = string.IsNullOrWhiteSpace(call.CallId)
                ? $"{call.Name}:{toolCalls.Count}"
                : call.CallId;
            toolCalls[key] = call;
        }
    }

    public static long? ReadInputTokens(AgentResponseUpdate update)
    {
        foreach (var content in update.Contents)
        {
            if (content is UsageContent { Details.InputTokenCount: long tokens })
            {
                return tokens;
            }
        }

        return null;
    }

    public static string ReadVisibleText(AgentResponseUpdate update)
    {
        if (update.Contents is not { Count: > 0 })
        {
            return update.Text;
        }

        var parts = update.Contents
            .Select(content => content switch
            {
                TextContent { Text: { Length: > 0 } text } => text,
                TextReasoningContent { Text: { Length: > 0 } text } => text,
                _ => null
            })
            .Where(text => text is not null);

        var combined = string.Concat(parts);
        return combined.Length > 0 ? combined : update.Text;
    }
}
