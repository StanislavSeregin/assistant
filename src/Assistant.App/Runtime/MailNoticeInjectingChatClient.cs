using Assistant.App.Mail;
using Assistant.App.Registry;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

/// <summary>
/// Injects pending mail notices as a system message before every model call,
/// which sits under FunctionInvokingChatClient and therefore runs between logical blocks.
/// </summary>
internal sealed class MailNoticeInjectingChatClient(IChatClient inner, NodeHandle agent)
    : DelegatingChatClient(inner)
{
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var prepared = InjectNotices(messages);
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
            return await base.GetResponseAsync(InjectNotices(messages), options, cancellationToken);
        }
        catch (Exception ex) when (NodeShutdown.IsBenign(ex, agent, cancellationToken))
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private IEnumerable<ChatMessage> InjectNotices(IEnumerable<ChatMessage> messages)
    {
        var notices = agent.Llm?.DrainPendingMailNotices() ?? [];
        if (notices.Count == 0)
        {
            return messages;
        }

        var list = messages as IList<ChatMessage> ?? messages.ToList();
        list.Add(new ChatMessage(ChatRole.System, FormatNotices(notices)));
        return list;
    }

    private static string FormatNotices(IReadOnlyList<MailNotice> notices)
    {
        var lines = notices.Select(n =>
            $"- id={n.MailId}; time={MailTimestamp.FormatUtc(n.Timestamp)}; from={n.From}; subject={n.Subject}");
        return
            "[SYSTEM] New mail arrived during your turn:" + Environment.NewLine
            + string.Join(Environment.NewLine, lines);
    }
}
