using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
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
        var credential = new ApiKeyCredential(_settings.ApiKey);
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(_settings.Endpoint),
            NetworkTimeout = _settings.NetworkTimeout
        };

        return new OpenAIClient(credential, clientOptions).GetChatClient(_settings.ModelName);
    }

    public ChatClientAgentRunOptions CreateRunOptions() =>
        new()
        {
            ChatOptions = new ChatOptions
            {
                RawRepresentationFactory = _ =>
                {
                    var completionOptions = new ChatCompletionOptions();
                    completionOptions.Patch.Set("$.stream_options.include_usage"u8, true);
                    return completionOptions;
                }
            }
        };
}
