using Assistant.App.UI.Abstractions;
using System;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Late-bound <see cref="IMailComposer"/> so the concrete pane can be created after toolkit Init.
/// </summary>
public sealed class MailComposerHub : IMailComposer
{
    private IMailComposer? _inner;
    private EventHandler<MailComposeRequest>? _sendRequested;

    public event EventHandler<MailComposeRequest>? SendRequested
    {
        add => _sendRequested += value;
        remove => _sendRequested -= value;
    }

    public string Subject
    {
        get => _inner?.Subject ?? string.Empty;
        set
        {
            if (_inner is not null)
            {
                _inner.Subject = value;
            }
        }
    }

    public void Attach(IMailComposer inner)
    {
        if (_inner is not null)
        {
            _inner.SendRequested -= Forward;
        }

        _inner = inner;
        _inner.SendRequested += Forward;
    }

    public void ClearBody() => _inner?.ClearBody();

    public void FocusBody() => _inner?.FocusBody();

    private void Forward(object? sender, MailComposeRequest request) =>
        _sendRequested?.Invoke(sender, request);
}
