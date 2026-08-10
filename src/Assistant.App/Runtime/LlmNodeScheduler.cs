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
        try
        {
            await foreach (var node in registry.LlmRegistered.ReadAllAsync(stoppingToken))
            {
                _ = ObserveNodeLoopAsync(node, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown cancelled the registration channel read.
        }
    }

    private async Task ObserveNodeLoopAsync(NodeHandle node, CancellationToken stoppingToken)
    {
        try
        {
            await RunNodeLoopAsync(node, stoppingToken);
        }
        catch (Exception ex) when (NodeShutdown.IsBenign(ex, node, stoppingToken))
        {
            // Mid-stream abort after dispose/shutdown must not surface as an error.
        }
        catch (Exception ex)
        {
            lifecycle.Publish(new ErrorEvent(node.Name, ex.Message));
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
                        () => llm.NeedsResumeTurn
                            ? turnRunner.ResumeTurnAsync(node, linked.Token)
                            : turnRunner.RunTurnAsync(node, linked.Token),
                        linked.Token);
                }
                catch (Exception ex) when (NodeShutdown.IsBenign(ex, node, linked.Token))
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
                    if (!node.IsDisposed
                        && (node.Inbox.HasMail() || TurnContinuation.HasOpenChecklist(node)))
                    {
                        llm.RequestWake();
                    }
                }
            }
        }
        catch (Exception ex) when (NodeShutdown.IsBenign(ex, node, linked.Token))
        {
            // Channel completed / token cancelled on dispose or host stop.
        }
    }
}
