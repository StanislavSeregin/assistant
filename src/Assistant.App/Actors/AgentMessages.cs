using System;

namespace Assistant.App.Actors;

public enum ReplyKind
{
    Intermediate,
    Final
}

public enum InboundKind
{
    ParentMessage,
    ChildReply
}

public static class AgentMessages
{
    public record ParentMessage(
        string RequestId,
        string From,
        string Content);

    public record ChildReply(
        string RequestId,
        string FromChild,
        ReplyKind Kind,
        string Content,
        string? InResponseToPreview = null);

    public sealed record InboundMessage(
        InboundKind Kind,
        string RequestId,
        string From,
        string Content,
        ReplyKind? ReplyKind = null,
        string? InResponseToPreview = null,
        Guid TransactionId = default)
    {
        public static InboundMessage FromParent(ParentMessage message) =>
            new(
                InboundKind.ParentMessage,
                message.RequestId,
                message.From,
                message.Content,
                TransactionId: Guid.NewGuid());

        public static InboundMessage FromChild(ChildReply message) =>
            new(
                InboundKind.ChildReply,
                message.RequestId,
                message.FromChild,
                message.Content,
                message.Kind,
                message.InResponseToPreview,
                Guid.NewGuid());

        public string FormatForPrompt()
        {
            if (Kind == InboundKind.ParentMessage)
            {
                return $"""
                    [Message from your parent {From}]
                    requestId: {RequestId}
                    ---
                    {Content}
                    """;
            }

            var preview = string.IsNullOrWhiteSpace(InResponseToPreview)
                ? string.Empty
                : $"{Environment.NewLine}inResponseTo: {InResponseToPreview}";

            return $"""
                [Subagent reply]
                from: {From}
                requestId: {RequestId}
                kind: {ReplyKind}{preview}
                ---
                {Content}
                """;
        }
    }
}
