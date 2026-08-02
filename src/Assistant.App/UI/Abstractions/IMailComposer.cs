using System;

namespace Assistant.App.UI.Abstractions;

/// <summary>
/// User mail composition surface. Implementations raise <see cref="SendRequested"/>;
/// a host service posts to <c>MailService</c>.
/// </summary>
public interface IMailComposer
{
    event EventHandler<MailComposeRequest>? SendRequested;

    string Subject { get; set; }

    void ClearBody();

    void FocusBody();
}

public sealed class MailComposeRequest(string subject, string body)
{
    public string Subject { get; } = subject;
    public string Body { get; } = body;
}
