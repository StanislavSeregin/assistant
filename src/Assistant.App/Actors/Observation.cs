using System;

namespace Assistant.App.Actors;

/// <summary>
/// UI observation channel — progressive render only. Never delivers work to agents.
/// </summary>
public static class Observation
{
    public interface IEvent;

    public record ThinkingStarted(string Agent, Guid StreamId) : IEvent;

    public record ThinkingDelta(string Agent, Guid StreamId, string Text) : IEvent;

    public record ThinkingCompleted(string Agent, Guid StreamId, long? InputTokens = null) : IEvent;

    public record OutboundStarted(string From, string? To, Guid StreamId, bool IsForUser) : IEvent;

    public record OutboundDelta(Guid StreamId, string Text) : IEvent;

    public record OutboundCompleted(Guid StreamId, long? InputTokens = null) : IEvent;

    public record ToolCallLogged(string Agent, string ToolName, string Payload) : IEvent;

    public record NudgeLogged(string Agent, string Message) : IEvent;

    public record ErrorLogged(string Agent, string Message) : IEvent;
}
