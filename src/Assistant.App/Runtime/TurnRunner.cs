using Assistant.App.Lifecycle;
using Assistant.App.Persistence;
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
    ILifecycleSink lifecycle,
    SessionCheckpoint checkpoint)
{
    private readonly ConcurrentDictionary<string, byte> _invokedToolCalls = new();
    private readonly AsyncLocal<TurnScope?> _scope = new();

    private sealed class TurnScope(NodeHandle agent, TurnActivity activity)
    {
        public NodeHandle Agent { get; } = agent;
        public TurnActivity Activity { get; } = activity;
        public Guid? ThinkingStreamId { get; set; }
    }

    public Task RunTurnAsync(NodeHandle agent, CancellationToken cancellationToken) =>
        WithTurnAsync(
            agent,
            new TurnActivity(),
            async activity =>
            {
                // Turn input must be User role — System is often dropped and the run returns empty.
                var turnHistoryStart = GetHistoryCount(agent);
                var (wake, listedMailIds) = TurnPromptBuilder.BuildWakeMessage(agent);
                lifecycle.Publish(new TurnWake(agent.Name, wake.Text));
                // Mail that arrived after TryBeginRun may have been queued as mid-turn notices
                // while still appearing in the wake inbox list — drop those duplicates.
                DiscardWakeCoveredMailNotifications(agent, listedMailIds);
                await RunModelAsync(agent, wake, activity, cancellationToken);
                await ContinueAfterWakeAsync(agent, activity, turnHistoryStart, cancellationToken);
            },
            cancellationToken);

    /// <summary>
    /// Resume a turn after process restore. Does not inject a new wake — prior wake is already in history.
    /// </summary>
    public Task ResumeTurnAsync(NodeHandle agent, CancellationToken cancellationToken)
    {
        var llm = RequireBoundLlm(agent);
        var activity = TurnActivity.FromPersisted(
            llm.PersistedDidHandleMail,
            llm.PersistedDidCommitContext);
        llm.NeedsResumeTurn = false;

        return WithTurnAsync(
            agent,
            activity,
            async act =>
            {
                if (act.DidCommitContext)
                {
                    return;
                }

                var historyStart = GetHistoryCount(agent);
                if (!act.DidHandleMail && agent.Inbox.HasMail())
                {
                    var notice = TurnPromptBuilder.BuildFallbackMailNotice(agent);
                    var systemNotice = "[SYSTEM]" + Environment.NewLine + notice;
                    lifecycle.Publish(new SupportAdvice(agent.Name, systemNotice));
                    await RunModelAsync(
                        agent,
                        new ChatMessage(ChatRole.User, systemNotice),
                        act,
                        cancellationToken);
                }
                else if (!act.DidHandleMail)
                {
                    // History exists but flags say mail unsettled and inbox empty — treat mail as done.
                    act.MarkMailHandled();
                }

                await ContinueAfterWakeAsync(agent, act, historyStart, cancellationToken);
            },
            cancellationToken);
    }

    private async Task WithTurnAsync(
        NodeHandle agent,
        TurnActivity activity,
        Func<TurnActivity, Task> body,
        CancellationToken cancellationToken)
    {
        _ = RequireBoundLlm(agent);
        _scope.Value = new TurnScope(agent, activity);
        lifecycle.Publish(new TurnStarted(agent.Name));

        try
        {
            await body(activity);
        }
        catch (Exception) when (NodeShutdown.IsStopped(agent, cancellationToken))
        {
            // Dispose / shutdown aborted the turn; do not surface as agent error.
        }
        finally
        {
            CompleteThinking(agent.Name);
            _scope.Value = null;
            lifecycle.Publish(new TurnEnded(agent.Name));
        }
    }

    private static LlmNodeRuntime RequireBoundLlm(NodeHandle agent)
    {
        var llm = agent.Llm
            ?? throw new InvalidOperationException($"Node '{agent.Name}' is not an LLM node.");
        if (llm.Agent is null || llm.Session is null)
        {
            throw new InvalidOperationException($"Agent '{agent.Name}' is not bootstrapped.");
        }

        return llm;
    }

    private static void DiscardWakeCoveredMailNotifications(
        NodeHandle agent,
        IReadOnlyList<string> listedMailIds)
    {
        if (agent.Llm is null || listedMailIds.Count == 0)
        {
            return;
        }

        var covered = new HashSet<string>(listedMailIds, StringComparer.Ordinal);
        agent.Llm.DiscardPendingSystemNotifications(notice =>
            notice.Kind == SystemNotificationKinds.Mail
            && notice.ItemId is not null
            && covered.Contains(notice.ItemId));
    }

    private async Task ContinueAfterWakeAsync(
        NodeHandle agent,
        TurnActivity activity,
        int turnHistoryStart,
        CancellationToken cancellationToken)
    {
        if (NodeShutdown.IsStopped(agent, cancellationToken))
        {
            return;
        }

        var mailSupportAttempt = 0;
        while (!activity.DidHandleMail && !NodeShutdown.IsStopped(agent, cancellationToken))
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

        if (NodeShutdown.IsStopped(agent, cancellationToken) || activity.DidCommitContext)
        {
            return;
        }

        var compactHistoryStart = GetHistoryCount(agent);
        var compactNudge = TurnPromptBuilder.BuildCompactNudgeMessage();
        lifecycle.Publish(new SupportAdvice(agent.Name, compactNudge.Text!));
        await RunModelAsync(agent, compactNudge, activity, cancellationToken);
        if (activity.DidCommitContext || NodeShutdown.IsStopped(agent, cancellationToken))
        {
            return;
        }

        var compactSupportAttempt = 0;
        while (!activity.DidCommitContext && !NodeShutdown.IsStopped(agent, cancellationToken))
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

        object? result;
        try
        {
            result = await invocation.Function.InvokeAsync(invocation.Arguments, cancellationToken);
        }
        catch (Exception ex) when (ShouldReturnToolError(ex, scope?.Agent, cancellationToken))
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;

            if (shouldPublish)
            {
                // Arg-bearing calls already logged intent; surface the failure explicitly.
                // No-arg calls still need a ToolCalled line (normally published after success).
                if (hasArguments)
                {
                    lifecycle.Publish(new ErrorEvent(
                        agentName,
                        $"{invocation.Function.Name}: {message}"));
                }
                else
                {
                    PublishToolCall(
                        agentName,
                        invocation.Function.Name,
                        callId,
                        origin,
                        result: message);
                }
            }

            return message;
        }

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

    private static bool ShouldReturnToolError(
        Exception ex,
        NodeHandle? agent,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (agent is not null && NodeShutdown.IsBenign(ex, agent, cancellationToken))
        {
            return false;
        }

        return true;
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
            try
            {
                await foreach (var update in llm.Agent.RunStreamingAsync(
                    input,
                    llm.Session,
                    chatClientFactory.CreateRunOptions(tools.BuildTools(agent, activity)),
                    cancellationToken))
                {
                    if (NodeShutdown.IsStopped(agent, cancellationToken))
                    {
                        return;
                    }

                    updates.Add(update);
                    inputTokens = ToolCallFormatting.ReadInputTokens(update) ?? inputTokens;
                    ToolCallFormatting.CollectToolCalls(update, pendingToolCalls);

                    var text = ToolCallFormatting.ReadVisibleText(update);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        EmitThinking(agent.Name, text);
                    }
                }
            }
            catch (Exception) when (NodeShutdown.IsStopped(agent, cancellationToken))
            {
                // Transport abort still leaked past the chat-client wrapper.
                return;
            }

            if (NodeShutdown.IsStopped(agent, cancellationToken))
            {
                return;
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
            if (!agent.IsDisposed)
            {
                PublishTurnUsage(agent.Name, inputTokens);
                // Block boundary: history is flushed; persist whole messages array + StateBag.
                checkpoint.CheckpointSession(agent, activity);
            }
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
