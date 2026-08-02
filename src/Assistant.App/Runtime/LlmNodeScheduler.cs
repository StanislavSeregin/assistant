using Assistant.App.Lifecycle;
using Assistant.App.Registry;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Runtime;

/// <summary>
/// Runs wake/turn loops for LLM nodes only. Human nodes are reactive via UI.
/// </summary>
public sealed class LlmNodeScheduler(
    NodeRegistry registry,
    ModelSlotLimiter slots,
    TurnRunner turnRunner,
    ILifecycleSink lifecycle) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var node in registry.LlmRegistered.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(
                () => RunNodeLoopAsync(node, stoppingToken),
                CancellationToken.None);
        }
    }

    private async Task RunNodeLoopAsync(NodeHandle node, CancellationToken stoppingToken)
    {
        var llm = node.Llm
            ?? throw new InvalidOperationException($"Node '{node.Name}' has no LLM runtime.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            node.Lifetime.Token);

        try
        {
            await foreach (var wake in llm.WakeChannel.Reader.ReadAllAsync(linked.Token))
            {
                _ = wake;
                if (node.IsDisposed)
                {
                    break;
                }

                while (llm.WakeChannel.Reader.TryRead(out _))
                {
                }

                if (!llm.TryBeginRun())
                {
                    continue;
                }

                try
                {
                    await slots.RunAsync(
                        () => turnRunner.RunTurnAsync(node, linked.Token),
                        linked.Token);
                }
                catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lifecycle.Publish(new ErrorEvent(node.Name, ex.Message));
                }
                finally
                {
                    llm.EndRun();
                    if (!node.IsDisposed && node.Inbox.HasMail())
                    {
                        llm.RequestWake();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }
}
