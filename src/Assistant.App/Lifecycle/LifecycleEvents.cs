using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Assistant.App.Lifecycle;

public interface ILifecycleEvent;

public enum ToolCallOrigin
{
    Application,
    Framework,
    External
}

public record AgentSpawned(
    string Agent,
    string AgentId,
    string Parent,
    string Description,
    string Instructions,
    string? ParentDescription) : ILifecycleEvent;

public record AgentDisposed(string Agent, string Parent) : ILifecycleEvent;

public record TurnStarted(string Agent) : ILifecycleEvent;

public record TurnWake(string Agent, string Message) : ILifecycleEvent;

public record TurnEnded(string Agent) : ILifecycleEvent;

public record ThinkingStarted(string Agent, Guid StreamId) : ILifecycleEvent;

public record ThinkingDelta(string Agent, Guid StreamId, string Text) : ILifecycleEvent;

public record ThinkingCompleted(
    string Agent,
    Guid StreamId,
    long? InputTokens = null) : ILifecycleEvent;

public record MailSent(
    string From,
    string To,
    string MailId,
    string Subject,
    string Body,
    bool IsReply) : ILifecycleEvent;

public record MailReceived(
    string Agent,
    string MailId,
    string From,
    string Subject,
    bool IsFromParent,
    DateTime Timestamp) : ILifecycleEvent;

public record MailRead(
    string Agent,
    string MailId,
    string From,
    string Subject,
    string Body,
    DateTime Timestamp) : ILifecycleEvent;

public record MailReplied(string Agent, string MailId) : ILifecycleEvent;

public record MailDeleted(string Agent, string MailId) : ILifecycleEvent;

public record MailPurged(string Agent, string FromAgent, int Count) : ILifecycleEvent;

public record ToolCalled(
    string Agent,
    string ToolName,
    ToolCallOrigin Origin,
    IReadOnlyDictionary<string, string?>? Arguments = null,
    string? CallId = null,
    string? Result = null) : ILifecycleEvent;

public record SupportAdvice(string Agent, string Message) : ILifecycleEvent;

public record ContextCommitted(string Agent, string Handoff, int HandoffCharacters) : ILifecycleEvent;

public record ErrorEvent(string Agent, string Message) : ILifecycleEvent;

public record UsageEvent(string Agent, long InputTokens) : ILifecycleEvent;

public sealed class LifecycleDrainBarrier : ILifecycleEvent
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _completion.Task;

    public void Complete() => _completion.TrySetResult();
}
