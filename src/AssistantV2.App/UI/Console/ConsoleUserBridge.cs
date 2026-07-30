using AssistantV2.App.Lifecycle;
using AssistantV2.App.Mail;
using AssistantV2.App.Registry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace AssistantV2.App.UI.Console;

/// <summary>
/// UI adaptor: only new user mails with a default subject. Not part of agent core.
/// </summary>
public sealed class ConsoleUserBridge(
    IUserInput userInput,
    MailService mail,
    AgentRegistry registry,
    ILifecycleSink lifecycle,
    IOptions<Settings> settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested && registry.Root?.Agent is null)
        {
            await Task.Delay(50, stoppingToken);
        }

        var subject = settings.Value.UserMailSubject;

        while (!stoppingToken.IsCancellationRequested)
        {
            await lifecycle.DrainAsync(stoppingToken);
            var input = await userInput.ReadAsync(stoppingToken);
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            mail.WriteFromUser(input, subject);
            await WaitUntilAgentsQuietAsync(stoppingToken);
        }
    }

    private async Task WaitUntilAgentsQuietAsync(CancellationToken cancellationToken)
    {
        var idleRounds = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsBusy())
            {
                idleRounds = 0;
            }
            else
            {
                idleRounds++;
                // Settle across EndRun → RequestWake(HasMail).
                if (idleRounds >= 2)
                {
                    return;
                }
            }

            await Task.Delay(50, cancellationToken);
        }
    }

    private bool IsBusy()
    {
        foreach (var agent in registry.All())
        {
            if (agent.State == AgentRunState.Disposed)
            {
                continue;
            }

            if (agent.State == AgentRunState.Running || agent.Inbox.HasMail())
            {
                return true;
            }
        }

        return false;
    }
}
