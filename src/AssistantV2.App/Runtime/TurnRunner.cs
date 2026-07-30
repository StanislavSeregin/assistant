using AssistantV2.App.Lifecycle;
using AssistantV2.App.Mail;
using AssistantV2.App.Registry;
using AssistantV2.App.Support;
using AssistantV2.App.Tools;
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

namespace AssistantV2.App.Runtime;

public sealed class TurnRunner(
    ChatClientFactory chatClientFactory,
    AgentMailTools tools,
    MailTurnSupport mailTurnSupport,
    ILifecycleSink lifecycle)
{
    private static readonly JsonSerializerOptions ToolArgumentJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly ConcurrentDictionary<string, byte> _invokedToolCalls = new();
    private readonly AsyncLocal<TurnScope?> _scope = new();

    private sealed class TurnScope(AgentHandle agent, TurnActivity activity)
    {
        public AgentHandle Agent { get; } = agent;
        public TurnActivity Activity { get; } = activity;
        public Guid? ThinkingStreamId { get; set; }
    }

    public async Task RunTurnAsync(AgentHandle agent, CancellationToken cancellationToken)
    {
        if (agent.Agent is null || agent.Session is null)
        {
            throw new InvalidOperationException($"Agent '{agent.Name}' is not bootstrapped.");
        }

        var activity = new TurnActivity();
        _scope.Value = new TurnScope(agent, activity);
        lifecycle.Publish(new TurnStarted(agent.Name));

        try
        {
            // Turn input must be User role — System is often dropped and the run returns empty.
            var turnHistoryStart = GetHistoryCount(agent);
            var wake = BuildWakeMessage(agent);
            lifecycle.Publish(new TurnWake(agent.Name, wake.Text));
            await RunModelAsync(agent, wake, activity, cancellationToken);

            var supportAttempt = 0;
            while (!activity.DidHandleMail && !cancellationToken.IsCancellationRequested)
            {
                supportAttempt++;
                var advice = await mailTurnSupport.AdviseAsync(
                    agent,
                    turnHistoryStart,
                    supportAttempt,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(advice))
                {
                    advice = BuildFallbackSystemNotice(agent);
                }

                var systemNotice = "[SYSTEM]" + Environment.NewLine + advice;
                lifecycle.Publish(new SupportAdvice(agent.Name, systemNotice));
                await RunModelAsync(
                    agent,
                    new ChatMessage(ChatRole.User, systemNotice),
                    activity,
                    cancellationToken);
            }
        }
        finally
        {
            CompleteThinking(agent.Name);
            _scope.Value = null;
            lifecycle.Publish(new TurnEnded(agent.Name));
        }
    }

    public async ValueTask<object?> InvokeToolAsync(
        FunctionInvocationContext invocation,
        CancellationToken cancellationToken)
    {
        var scope = _scope.Value;
        var agentName = scope?.Agent.Name ?? "?";
        var callId = invocation.CallContent.CallId;
        var shouldPublish = string.IsNullOrWhiteSpace(callId) || _invokedToolCalls.TryAdd(callId, 0);
        var origin = AgentMailTools.ApplicationToolNames.Contains(invocation.Function.Name)
            ? ToolCallOrigin.Application
            : ToolCallOrigin.Framework;
        var hasArguments = invocation.Arguments is { Count: > 0 };

        CompleteThinking(agentName);

        // Argument-bearing calls: show intent before side effects (MailSent, spawn, …).
        if (shouldPublish && hasArguments)
        {
            PublishToolCall(
                agentName,
                invocation.Function.Name,
                callId,
                origin,
                invocation.Arguments);
        }

        var result = await invocation.Function.InvokeAsync(invocation.Arguments, cancellationToken);

        // No-arg calls (ListInbox, GetRecipients, …): show what the agent received.
        if (shouldPublish && !hasArguments)
        {
            PublishToolCall(
                agentName,
                invocation.Function.Name,
                callId,
                origin,
                result: FormatToolResult(result));
        }

        return result;
    }

    private async Task RunModelAsync(
        AgentHandle agent,
        ChatMessage input,
        TurnActivity activity,
        CancellationToken cancellationToken)
    {
        _invokedToolCalls.Clear();
        long? inputTokens = null;
        var pendingToolCalls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        try
        {
            var updates = new List<AgentResponseUpdate>();
            await foreach (var update in agent.Agent!.RunStreamingAsync(
                input,
                agent.Session,
                chatClientFactory.CreateRunOptions(tools.BuildTools(agent, activity)),
                cancellationToken))
            {
                updates.Add(update);
                inputTokens = ReadInputTokens(update) ?? inputTokens;
                CollectToolCalls(update, pendingToolCalls);

                var text = ReadVisibleText(update);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    EmitThinking(agent.Name, text);
                }
            }

            if (inputTokens is null && updates.Count > 0)
            {
                inputTokens = updates.ToAgentResponse().Usage?.InputTokenCount;
            }
        }
        finally
        {
            PublishUninvokedToolCalls(agent.Name, pendingToolCalls.Values);
            _invokedToolCalls.Clear();
            PublishTurnUsage(agent.Name, inputTokens);
        }
    }

    private static int GetHistoryCount(AgentHandle agent) =>
        agent.Session is not null && agent.Session.TryGetInMemoryChatHistory(out var history)
            ? history.Count
            : 0;

    private static string BuildFallbackSystemNotice(AgentHandle agent)
    {
        var open = agent.Inbox.List();
        var parentMail = open.FirstOrDefault(item => item.IsFromParent) ?? open.FirstOrDefault();
        if (parentMail is null)
        {
            return "Turn incomplete: no mail was delivered. " +
                   "Call WriteMail or ReplyMail — free text is private and reaches no one.";
        }

        return $"Turn incomplete: inbox still has id={parentMail.Id} from={parentMail.From} " +
               $"subject={parentMail.Subject}. ReplyMail to that id with your answer — " +
               "free text is private and reaches no one.";
    }

    private static ChatMessage BuildWakeMessage(AgentHandle agent)
    {
        var open = agent.Inbox.List();
        if (open.Count == 0)
        {
            throw new InvalidOperationException(
                $"Agent '{agent.Name}' started a turn with an empty inbox.");
        }

        var lines = open.Select(i =>
            $"- id={i.Id}; time={MailTimestamp.FormatUtc(i.Timestamp)}; from={i.From}; " +
            $"subject={i.Subject}{MailStatusDisplay.ListSuffix(i.Status)}");
        return new ChatMessage(
            ChatRole.User,
            "[SYSTEM] You have mail. Solve what was asked; deliver with ReplyMail or WriteMail " +
            "(only those are seen):"
            + Environment.NewLine
            + string.Join(Environment.NewLine, lines));
    }

    private void EmitThinking(string agentName, string text)
    {
        var scope = _scope.Value;
        if (scope is null)
        {
            return;
        }

        if (scope.ThinkingStreamId is null)
        {
            scope.ThinkingStreamId = Guid.NewGuid();
            lifecycle.Publish(new ThinkingStarted(agentName, scope.ThinkingStreamId.Value));
        }

        lifecycle.Publish(new ThinkingDelta(agentName, scope.ThinkingStreamId.Value, text));
    }

    private void CompleteThinking(string agentName, long? inputTokens = null)
    {
        var scope = _scope.Value;
        if (scope?.ThinkingStreamId is not { } streamId)
        {
            return;
        }

        lifecycle.Publish(new ThinkingCompleted(agentName, streamId, inputTokens));
        scope.ThinkingStreamId = null;
    }

    private void PublishTurnUsage(string agentName, long? inputTokens)
    {
        if (_scope.Value?.ThinkingStreamId is not null)
        {
            CompleteThinking(agentName, inputTokens);
            return;
        }

        if (inputTokens is long tokens)
        {
            lifecycle.Publish(new UsageEvent(agentName, tokens));
        }
    }

    private void PublishUninvokedToolCalls(string agentName, IEnumerable<FunctionCallContent> toolCalls)
    {
        foreach (var call in toolCalls)
        {
            if (!string.IsNullOrWhiteSpace(call.CallId)
                && _invokedToolCalls.TryRemove(call.CallId, out _))
            {
                continue;
            }

            var origin = AgentMailTools.ApplicationToolNames.Contains(call.Name)
                ? ToolCallOrigin.Application
                : ToolCallOrigin.External;
            PublishToolCall(agentName, call.Name, call.CallId, origin, call.Arguments);
        }
    }

    private void PublishToolCall(
        string agentName,
        string toolName,
        string? callId,
        ToolCallOrigin origin,
        IEnumerable<KeyValuePair<string, object?>>? arguments = null,
        string? result = null)
    {
        IReadOnlyDictionary<string, string?>? formattedArguments = null;
        if (arguments is not null)
        {
            formattedArguments = arguments.ToDictionary(
                argument => argument.Key,
                argument => FormatToolArgument(argument.Value),
                StringComparer.Ordinal);
            if (formattedArguments.Count == 0)
            {
                formattedArguments = null;
            }
        }

        lifecycle.Publish(new ToolCalled(
            agentName,
            toolName,
            origin,
            formattedArguments,
            callId,
            result));
    }

    private static string? FormatToolResult(object? result) =>
        result switch
        {
            null => null,
            string text => string.IsNullOrWhiteSpace(text) ? null : text,
            _ => result.ToString()
        };

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
