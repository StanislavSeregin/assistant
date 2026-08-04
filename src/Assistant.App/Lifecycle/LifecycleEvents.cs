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

public record NodeSpawned(
    string Node,
    string NodeId,
    string Parent,
    string Description,
    string Instructions,
    string? ParentDescription) : ILifecycleEvent;

public record NodeDisposed(string Node, string Parent) : ILifecycleEvent;

public record TurnStarted(string Node) : ILifecycleEvent;

public record TurnWake(string Node, string Message) : ILifecycleEvent;

public record TurnEnded(string Node) : ILifecycleEvent;

public record ThinkingStarted(string Node, Guid StreamId) : ILifecycleEvent;

public record ThinkingDelta(string Node, Guid StreamId, string Text) : ILifecycleEvent;

public record ThinkingCompleted(
    string Node,
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
    string Node,
    string MailId,
    string From,
    string Subject,
    bool IsFromParent,
    DateTime Timestamp) : ILifecycleEvent;

public record MailRead(
    string Node,
    string MailId,
    string From,
    string Subject,
    string Body,
    DateTime Timestamp) : ILifecycleEvent;

public record MailReplied(string Node, string MailId) : ILifecycleEvent;

public record MailDeleted(string Node, string MailId) : ILifecycleEvent;

public record MailPurged(string Node, string FromNode, int Count) : ILifecycleEvent;

public record ToolCalled(
    string Node,
    string ToolName,
    ToolCallOrigin Origin,
    IReadOnlyDictionary<string, string?>? Arguments = null,
    string? CallId = null,
    string? Result = null) : ILifecycleEvent;

public record SupportAdvice(string Node, string Message) : ILifecycleEvent;

public record SystemNotificationInjected(string Node, string Message) : ILifecycleEvent;

public record ContextCommitted(string Node, string Handoff, int HandoffCharacters) : ILifecycleEvent;

public record ErrorEvent(string Node, string Message) : ILifecycleEvent;

public record UsageEvent(string Node, long InputTokens) : ILifecycleEvent;

public sealed class LifecycleDrainBarrier : ILifecycleEvent
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _completion.Task;

    public void Complete() => _completion.TrySetResult();
}
