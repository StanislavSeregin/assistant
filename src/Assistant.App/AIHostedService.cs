using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App;

public class AIHostedService(
    ISubject<KekMessage?> kekMessageSubject,
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
            .CreateAIAgent(instructions: "You are good at telling jokes.", name: "Joker");

        var response = await agent.RunAsync("Tell me a joke about a pirate.", cancellationToken: stoppingToken);

        kekMessageSubject.OnNext(new KekMessage(response.ToString()));
    }
}
