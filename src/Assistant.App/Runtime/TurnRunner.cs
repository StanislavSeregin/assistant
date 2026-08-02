using Assistant.App.Lifecycle;
using Assistant.App.Registry;
using Assistant.App.Support;
using Assistant.App.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

public sealed class TurnRunner(
    ChatClientFactory chatClientFactory,
    AgentMailTools tools,
    TurnSupportAdvisor turnSupport,
    ILifecycleSink lifecycle)
{
    private readonly ConcurrentDictionary<string, byte> _invokedToolCalls = new();
    private readonly AsyncLocal<TurnScope?> _scope = new();

    private sealed class TurnScope(NodeHandle agent, TurnActivity activity)
    {
        public NodeHandle Agent { get; } = agent;
        public TurnActivity Activity { get; } = activity;
        public Guid? ThinkingStreamId { get; set; }
    }

    public async Task RunTurnAsync(NodeHandle agent, CancellationToken cancellationToken)
    {
        var llm = agent.Llm
            ?? throw new InvalidOperationException($"Node '{agent.Name}' is not an LLM node.");
        if (llm.Agent is null || llm.Session is null)
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
            var wake = TurnPromptBuilder.BuildWakeMessage(agent);
            lifecycle.Publish(new TurnWake(agent.Name, wake.Text));
            await RunModelAsync(agent, wake, activity, cancellationToken);

            var mailSupportAttempt = 0;
            while (!activity.DidHandleMail && !cancellationToken.IsCancellationRequested)
            {
                mailSupportAttempt++;
                var advice = await turnSupport.AdviseAsync(
                    TurnSupportMode.Mail,
                    agent,
                    turnHistoryStart,
                    mailSupportAttempt,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(advice))
                {
                    advice = TurnPromptBuilder.BuildFallbackMailNotice(agent);
                }

                var systemNotice = "[SYSTEM]" + Environment.NewLine + advice;
                lifecycle.Publish(new SupportAdvice(agent.Name, systemNotice));
                await RunModelAsync(
                    agent,
                    new ChatMessage(ChatRole.User, systemNotice),
                    activity,
                    cancellationToken);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // Agent may CommitContext in the same model run right after mail tools.
            if (activity.DidCommitContext)
            {
                return;
            }

            var compactHistoryStart = GetHistoryCount(agent);
            var compactNudge = TurnPromptBuilder.BuildCompactNudgeMessage();
            lifecycle.Publish(new SupportAdvice(agent.Name, compactNudge.Text!));
            await RunModelAsync(agent, compactNudge, activity, cancellationToken);
            if (activity.DidCommitContext)
            {
                return;
            }

            var compactSupportAttempt = 0;
            while (!activity.DidCommitContext && !cancellationToken.IsCancellationRequested)
            {
                compactSupportAttempt++;
                var advice = await turnSupport.AdviseAsync(
                    TurnSupportMode.Compact,
                    agent,
                    compactHistoryStart,
                    compactSupportAttempt,
                    cancellationToken);
                if (string.IsNullOrWhiteSpace(advice))
                {
                    advice = TurnPromptBuilder.BuildFallbackCompactNotice();
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
                result: ToolCallFormatting.FormatResult(result));
        }

        return result;
    }

    private async Task RunModelAsync(
        NodeHandle agent,
        ChatMessage input,
        TurnActivity activity,
        CancellationToken cancellationToken)
    {
        var llm = agent.Llm
            ?? throw new InvalidOperationException($"Node '{agent.Name}' is not an LLM node.");
        _invokedToolCalls.Clear();
        long? inputTokens = null;
        var pendingToolCalls = new Dictionary<string, FunctionCallContent>(StringComparer.Ordinal);

        try
        {
            if (llm.Agent!.GetService<FunctionInvokingChatClient>() is { } functionClient)
            {
                functionClient.FunctionInvoker = InvokeToolAsync;
            }

            var updates = new List<AgentResponseUpdate>();
            await foreach (var update in llm.Agent.RunStreamingAsync(
                input,
                llm.Session,
                chatClientFactory.CreateRunOptions(tools.BuildTools(agent, activity)),
                cancellationToken))
            {
                updates.Add(update);
                inputTokens = ToolCallFormatting.ReadInputTokens(update) ?? inputTokens;
                ToolCallFormatting.CollectToolCalls(update, pendingToolCalls);

                var text = ToolCallFormatting.ReadVisibleText(update);
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

    private static int GetHistoryCount(NodeHandle agent) =>
        agent.Llm?.Session is not null && agent.Llm.Session.TryGetInMemoryChatHistory(out var history)
            ? history.Count
            : 0;

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
        lifecycle.Publish(new ToolCalled(
            agentName,
            toolName,
            origin,
            ToolCallFormatting.FormatArguments(arguments),
            callId,
            result));
    }
}
