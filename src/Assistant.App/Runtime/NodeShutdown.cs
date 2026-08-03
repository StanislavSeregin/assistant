using Assistant.App.Registry;
using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Assistant.App.Runtime;

/// <summary>
/// Shared dispose/shutdown checks for turn loops and streaming chat clients.
/// Dispose cancels <see cref="NodeHandle.Lifetime"/> mid-SSE; the OpenAI SDK then
/// surfaces <see cref="TaskCanceledException"/> (often wrapping IO/socket abort).
/// </summary>
internal static class NodeShutdown
{
    public static bool IsStopped(NodeHandle node, CancellationToken token) =>
        node.IsDisposed || token.IsCancellationRequested;

    public static bool IsBenign(Exception ex, NodeHandle node, CancellationToken token)
    {
        if (!IsStopped(node, token))
        {
            return false;
        }

        if (ex is AggregateException aggregate)
        {
            if (aggregate.InnerExceptions.Count == 0)
            {
                return false;
            }

            foreach (var inner in aggregate.InnerExceptions)
            {
                if (!IsBenignException(inner))
                {
                    return false;
                }
            }

            return true;
        }

        return IsBenignException(ex);
    }

    private static bool IsBenignException(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is OperationCanceledException
                or ObjectDisposedException
                or IOException
                or SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
