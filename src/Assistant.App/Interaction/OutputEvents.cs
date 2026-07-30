using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Assistant.App.Interaction;

public interface IOutputEvent;

public enum MessageKind
{
    Message,
    Intermediate,
    Final
}

public enum ToolCallOrigin
{
    Application,
    Framework,
    External
}

public record ThinkingStarted(string Agent, Guid StreamId) : IOutputEvent;

public record ThinkingDelta(string Agent, Guid StreamId, string Text) : IOutputEvent;

public record ThinkingCompleted(
    string Agent,
    Guid StreamId,
    long? InputTokens = null,
    DateTime? EndedAt = null) : IOutputEvent;

public record MessageStarted(
    string From,
    string? To,
    Guid StreamId,
    bool IsForUser,
    DateTime StartedAt,
    Guid? TransactionId,
    string? RequestId,
    MessageKind Kind) : IOutputEvent;

public record MessageDelta(Guid StreamId, string Text) : IOutputEvent;

public record MessageCompleted(Guid StreamId, long? InputTokens = null) : IOutputEvent;

public record ToolCalled(
    string Agent,
    string ToolName,
    ToolCallOrigin Origin,
    IReadOnlyDictionary<string, string?>? Arguments = null,
    string? CallId = null) : IOutputEvent;

public record NudgeOutput(string Agent, string Message) : IOutputEvent;

public record ErrorOutput(string Agent, string Message) : IOutputEvent;

public record CompactionStarted(string Agent, int Version, int HistoryMessages) : IOutputEvent;

public record CompactionCompleted(
    string Agent,
    int Version,
    string Snapshot,
    int BeforeMessages,
    int BeforeCharacters,
    int AfterCharacters,
    TimeSpan Duration,
    long? InputTokens,
    long? OutputTokens) : IOutputEvent;

public record CompactionFailed(string Agent, string Message) : IOutputEvent;

public record UsageOutput(string Agent, long InputTokens) : IOutputEvent;

/// <summary>
/// Marker processed by the output pump: completes when all prior events have been handled.
/// </summary>
public sealed class OutputDrainBarrier : IOutputEvent
{
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _completion.Task;

    public void Complete() => _completion.TrySetResult();
}
