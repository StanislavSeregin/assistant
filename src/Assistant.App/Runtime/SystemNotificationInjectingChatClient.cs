using Assistant.App.Lifecycle;
using Assistant.App.Registry;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

/// <summary>
/// Injects pending system notifications as one system message before every model call,
/// which sits under FunctionInvokingChatClient and therefore runs between logical blocks.
/// </summary>
internal sealed class SystemNotificationInjectingChatClient(
    IChatClient inner,
    NodeHandle agent,
    ILifecycleSink lifecycle)
    : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = InjectNotifications(messages);
        var enumerator = base.GetStreamingResponseAsync(prepared, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        try
        {
            while (true)
            {
                bool moved;
                try
                {
                    moved = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (NodeShutdown.IsBenign(ex, agent, cancellationToken))
                {
                    // Dispose cancels Lifetime mid-SSE; end the stream instead of faulting
                    // the Agents pipeline (which surfaces TaskCanceledException as an error).
                    yield break;
                }

                if (!moved)
                {
                    yield break;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            try
            {
                await enumerator.DisposeAsync();
            }
            catch (Exception) when (NodeShutdown.IsStopped(agent, cancellationToken))
            {
                // Enumerator cleanup can re-throw the same transport abort.
            }
        }
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.GetResponseAsync(InjectNotifications(messages), options, cancellationToken);
        }
        catch (Exception ex) when (NodeShutdown.IsBenign(ex, agent, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private IEnumerable<ChatMessage> InjectNotifications(IEnumerable<ChatMessage> messages)
    {
        var notices = agent.Llm?.DrainPendingSystemNotifications() ?? [];
        if (notices.Count == 0)
        {
            return messages;
        }

        var text = FormatNotifications(notices);
        lifecycle.Publish(new SystemNotificationInjected(agent.Name, text));

        var list = messages as IList<ChatMessage> ?? messages.ToList();
        list.Add(new ChatMessage(ChatRole.System, text));
        return list;
    }

    private static string FormatNotifications(IReadOnlyList<SystemNotification> notices)
    {
        var sections = new List<(string Kind, List<string> Details)>();
        var indexByKind = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var notice in notices)
        {
            if (!indexByKind.TryGetValue(notice.Kind, out var index))
            {
                index = sections.Count;
                indexByKind[notice.Kind] = index;
                sections.Add((notice.Kind, []));
            }

            sections[index].Details.Add(notice.Detail);
        }

        var sb = new StringBuilder();
        sb.AppendLine("[SYSTEM] Notifications during your turn:");
        for (var i = 0; i < sections.Count; i++)
        {
            var (kind, details) = sections[i];
            sb.Append(kind);
            sb.Append(':');
            sb.AppendLine();
            foreach (var detail in details)
            {
                sb.Append("- ");
                sb.AppendLine(detail);
            }

            if (i < sections.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString().TrimEnd();
    }
}
