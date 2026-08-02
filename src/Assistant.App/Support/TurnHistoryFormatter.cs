using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Assistant.App.Support;

internal static class TurnHistoryFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string Format(IReadOnlyList<ChatMessage> messages)
    {
        if (messages.Count == 0)
        {
            return "(empty turn history)";
        }

        var builder = new StringBuilder();
        for (var i = 0; i < messages.Count; i++)
        {
            var message = messages[i];
            builder.Append("## Message ")
                .Append(i + 1)
                .Append(" (")
                .Append(message.Role)
                .AppendLine(")");

            if (message.Contents.Count == 0)
            {
                var text = message.Text;
                builder.AppendLine(string.IsNullOrWhiteSpace(text) ? "[empty]" : text);
                builder.AppendLine();
                continue;
            }

            foreach (var content in message.Contents)
            {
                switch (content)
                {
                    case TextContent { Text: { Length: > 0 } text }:
                        AppendBlock(builder, "text", text);
                        break;

                    case TextReasoningContent { Text: { Length: > 0 } text }:
                        AppendBlock(builder, "reasoning", text);
                        break;

                    case FunctionCallContent call:
                        builder.Append("[function_call] ")
                            .Append(call.Name)
                            .Append(' ')
                            .AppendLine(FormatValue(call.Arguments));
                        break;

                    case FunctionResultContent result:
                        builder.Append("[function_result] ")
                            .AppendLine(FormatValue(result.Result));
                        break;

                    case ErrorContent error:
                        AppendBlock(builder, "error", error.Message);
                        break;

                    default:
                        AppendBlock(builder, content.GetType().Name, content.ToString());
                        break;
                }
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendBlock(StringBuilder builder, string kind, string? text)
    {
        builder.Append('[')
            .Append(kind)
            .AppendLine("]");
        builder.AppendLine(text ?? string.Empty);
    }

    private static string FormatValue(object? value) =>
        value switch
        {
            null => "null",
            string text => text,
            JsonElement element => element.ToString(),
            _ => JsonSerializer.Serialize(value, JsonOptions)
        };
}
