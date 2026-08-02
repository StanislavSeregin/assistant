using System;

namespace Assistant.App.Mail;

public enum MailStatus
{
    New,
    Read
}

public static class MailStatusDisplay
{
    /// <summary>List/wake lines: only unread mail get an explicit marker at the end.</summary>
    public static string ListSuffix(MailStatus status) =>
        status == MailStatus.New ? "; status=NEW" : string.Empty;
}

public sealed class MailMessage
{
    public required string Id { get; init; }

    public required DateTime Timestamp { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }

    public required string Subject { get; init; }

    public required string Body { get; init; }

    public required bool IsFromParent { get; init; }

    public required Guid ThreadId { get; init; }

    public MailStatus Status { get; set; } = MailStatus.New;
}

public sealed record InboxItem(
    string Id,
    DateTime Timestamp,
    string From,
    string Subject,
    bool IsFromParent,
    MailStatus Status);
