using Microsoft.Extensions.AI;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Support;

/// <summary>
/// One-shot content-in / content-out model call. No tools, session, registry, or slot lease.
/// </summary>
public sealed class StatelessAgent(ChatClientFactory chatClientFactory)
{
    public async Task<string> RunAsync(
        string instructions,
        string content,
        CancellationToken cancellationToken,
        int? maxOutputTokens = null)
    {
        var response = await chatClientFactory.GetChatClient().GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, instructions),
                new ChatMessage(ChatRole.User, content)
            ],
            new ChatOptions
            {
                Temperature = 0,
                MaxOutputTokens = maxOutputTokens
            },
            cancellationToken);

        return (response.Text ?? string.Empty).Trim();
    }
}
