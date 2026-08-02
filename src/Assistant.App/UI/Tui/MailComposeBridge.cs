using Assistant.App.Mail;
using Assistant.App.UI.Abstractions;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.UI.Tui;

/// <summary>
/// Wires <see cref="IMailComposer.SendRequested"/> to <see cref="MailService"/> without blocking the UI.
/// </summary>
public sealed class MailComposeBridge(
    IMailComposer composer,
    MailService mail,
    ILogSink log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        composer.SendRequested += OnSendRequested;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        composer.SendRequested -= OnSendRequested;
        return Task.CompletedTask;
    }

    private void OnSendRequested(object? sender, MailComposeRequest request)
    {
        try
        {
            mail.WriteFromUser(request.Body, request.Subject);
        }
        catch (Exception ex)
        {
            log.BlankLine();
            log.Header("UI · send failed", LogTone.Red, DateTime.Now);
            log.BodyLine(ex.Message, LogTone.Red);
            log.Footer();
        }
    }
}
