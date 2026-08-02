using Assistant.App.Lifecycle;
using Assistant.App.Mail;
using Assistant.App.Registry;
using Assistant.App.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.UI.Console;

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
        await registry.WaitForRootAsync(stoppingToken);

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
            await registry.WaitUntilQuietAsync(stoppingToken);
        }
    }
}
