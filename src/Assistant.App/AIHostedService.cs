using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class AIHostedService(
    ISubject<IMessage?> kekMessageSubject,
    IOptions<Settings> options
) : BackgroundService
{
    private readonly Settings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var apiKeyCredential = new ApiKeyCredential(_settings.ApiKey);
        var openAIClientOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(_settings.Endpoint)
        };

        var agent = new OpenAIClient(apiKeyCredential, openAIClientOptions)
            .GetChatClient(_settings.ModelName)
            .CreateAIAgent(instructions: "You are good at telling jokes.", name: "Joker") ?? throw new InvalidOperationException();

        var liveContent = agent
            .RunStreamingAsync("Tell me a joke about a pirate.", cancellationToken: stoppingToken)
            .Where(update => !string.IsNullOrEmpty(update.Text))
            .Select(update => update.Text);

        kekMessageSubject.OnNext(new StreamingMessage("Assistant", liveContent));
    }
}
