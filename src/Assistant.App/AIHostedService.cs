using Microsoft.Agents.AI;
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
    ISubject<IAIEvent?> aiEventSubject,
    IObservable<IUIEvent> uiEventObservable,
    IOptions<Settings> options
) : BackgroundService
{
    private const string AGENT_NAME = "Фроська";

    private readonly Settings _settings = options.Value;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var apiKeyCredential = new ApiKeyCredential(_settings.ApiKey);
        var openAIClientOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(_settings.Endpoint)
        };

        var agent = new OpenAIClient(apiKeyCredential, openAIClientOptions)
            .GetChatClient(_settings.ModelName)
            .CreateAIAgent(instructions: "Ты полезный ассистент", name: AGENT_NAME) ?? throw new InvalidOperationException();

        var thread = agent.GetNewThread();
        uiEventObservable.Subscribe(async uiEvent =>
        {
            await (uiEvent switch
            {
                HumanMessage msg => HandleMessage(msg, agent, thread, stoppingToken),
                _ => Task.CompletedTask
            });
        });

        return Task.CompletedTask;
    }

    private async Task HandleMessage(HumanMessage humanMessage, ChatClientAgent agent, AgentThread? agentThread, CancellationToken cancellationToken)
    {
        var liveContent = agent
            .RunStreamingAsync(humanMessage.Text, agentThread, cancellationToken: cancellationToken)
            .Where(update => !string.IsNullOrEmpty(update.Text))
            .Select(update => update.Text);

        aiEventSubject.OnNext(new StreamingAIResponse(AGENT_NAME, liveContent));
    }
}
