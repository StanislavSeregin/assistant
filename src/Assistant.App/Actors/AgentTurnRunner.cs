using Assistant.App.Interaction;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal sealed class AgentTurnRunner(
    ChatClientFactory chatClientFactory,
    AgentConcurrencyLimiter concurrencyLimiter,
    IOutputEventSink output,
    AgentTurnTimeline timeline,
    AgentCollaborationTools tools)
{
    private static readonly JsonSerializerOptions ToolArgumentJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<string, byte> _invokedToolCalls = new();

    private string _agentName = string.Empty;

    public AIAgent Agent
    {
        get => field ?? throw new InvalidOperationException();
        set;
    }

    public AgentSession Session
    {
        get => field ?? throw new InvalidOperationException();
        set;
    }

    public void Bind(string agentName)
    {
        _agentName = agentName;
    }

    public async ValueTask<object?> InvokeToolAsync(
        FunctionInvocationContext invocation,
        CancellationToken cancellationToken)
    {
        var callId = invocation.CallContent.CallId;
        if (string.IsNullOrWhiteSpace(callId) || _invokedToolCalls.TryAdd(callId, 0))
        {
            PublishToolCall(
                invocation.Function.Name,
                callId,
                AgentCollaborationTools.ApplicationToolNames.Contains(invocation.Function.Name)
                    ? ToolCallOrigin.Application
                    : ToolCallOrigin.Framework,
                invocation.Arguments);
        }

        return await invocation.Function.InvokeAsync(invocation.Arguments, cancellationToken);
    }

    public Task RunAsync(
        ChatMessage chatMessage,
        bool canRespond,
        CancellationToken cancellationToken) =>
        concurrencyLimiter.RunAsync(async () =>
        {
            long? inputTokens = null;
            var pendingToolCalls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);
            _invokedToolCalls.Clear();
            timeline.BeginTurn();

            try
            {
                var updates = new List<AgentResponseUpdate>();
                await foreach (var update in Agent.RunStreamingAsync(
                    chatMessage,
                    Session,
                    chatClientFactory.CreateRunOptions(tools.BuildTools(canRespond)),
                    cancellationToken))
                {
                    // Final staged: drop further model output and end the run.
                    if (tools.Transaction?.ShouldEndModelRun == true)
                    {
                        break;
                    }

                    updates.Add(update);
                    inputTokens = ReadInputTokens(update) ?? inputTokens;
                    CollectToolCalls(update, pendingToolCalls);

                    var text = ReadVisibleText(update);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        timeline.EmitThinking(text);
                    }
                }

                if (inputTokens is null && updates.Count > 0)
                {
                    inputTokens = updates.ToAgentResponse().Usage?.InputTokenCount;
                }
            }
            finally
            {
                PublishUninvokedToolCalls(pendingToolCalls.Values);
                _invokedToolCalls.Clear();
                timeline.PublishTurnUsage(inputTokens);
            }
        }, cancellationToken);

    private void PublishUninvokedToolCalls(IEnumerable<FunctionCallContent> toolCalls)
    {
        foreach (var call in toolCalls)
        {
            if (!string.IsNullOrWhiteSpace(call.CallId)
                && _invokedToolCalls.TryRemove(call.CallId, out _))
            {
                continue;
            }

            var origin = AgentCollaborationTools.ApplicationToolNames.Contains(call.Name)
                ? ToolCallOrigin.Application
                : ToolCallOrigin.External;
            PublishToolCall(call.Name, call.CallId, origin, call.Arguments);
        }
    }

    private void PublishToolCall(
        string toolName,
        string? callId,
        ToolCallOrigin origin,
        IEnumerable<KeyValuePair<string, object?>>? arguments = null)
    {
        IReadOnlyDictionary<string, string?>? formattedArguments = null;
        if (origin == ToolCallOrigin.Application && arguments is not null)
        {
            formattedArguments = arguments.ToDictionary(
                argument => argument.Key,
                argument => FormatToolArgument(argument.Value),
                StringComparer.Ordinal);
        }

        output.Publish(new ToolCalled(
            _agentName,
            toolName,
            origin,
            formattedArguments,
            callId));
    }

    private static string? FormatToolArgument(object? value) =>
        value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null } => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => JsonSerializer.Serialize(element, ToolArgumentJsonOptions),
            _ => JsonSerializer.Serialize(value, ToolArgumentJsonOptions)
        };

    private static void CollectToolCalls(
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

    private static long? ReadInputTokens(AgentResponseUpdate update)
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

    private static string ReadVisibleText(AgentResponseUpdate update)
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
