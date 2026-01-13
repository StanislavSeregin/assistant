using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;

namespace Assistant.App;

public class ChatClientFactory(IOptions<Settings> options)
{
    private readonly Settings _settings = options.Value;

    public ChatClient GetChatClient()
    {
        var apiKeyCredential = new ApiKeyCredential(_settings.ApiKey);
        var openAIClientOptions = new OpenAIClientOptions()
        {
            Endpoint = new Uri(_settings.Endpoint)
        };

        var openAIClient = new OpenAIClient(apiKeyCredential, openAIClientOptions);
        return openAIClient.GetChatClient(_settings.ModelName);
    }
}
