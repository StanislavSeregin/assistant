using AssistantV2.App.Lifecycle;
using AssistantV2.App.Registry;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace AssistantV2.App.Runtime;

public sealed class AgentScheduler(
    AgentRegistry registry,
    ModelSlotLimiter slots,
    TurnRunner turnRunner,
    ILifecycleSink lifecycle) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var agent in registry.Registered.ReadAllAsync(stoppingToken))
        {
            _ = Task.Run(
                () => RunAgentLoopAsync(agent, stoppingToken),
                CancellationToken.None);
        }
    }

    private async Task RunAgentLoopAsync(AgentHandle agent, CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            agent.Lifetime.Token);

        try
        {
            await foreach (var wake in agent.WakeChannel.Reader.ReadAllAsync(linked.Token))
            {
                _ = wake;
                if (agent.State == AgentRunState.Disposed)
                {
                    break;
                }

                while (agent.WakeChannel.Reader.TryRead(out _))
                {
                }

                if (!agent.TryBeginRun())
                {
                    continue;
                }

                try
                {
                    await slots.RunAsync(
                        () => turnRunner.RunTurnAsync(agent, linked.Token),
                        linked.Token);
                }
                catch (OperationCanceledException) when (linked.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    lifecycle.Publish(new ErrorEvent(agent.Name, ex.Message));
                }
                finally
                {
                    agent.EndRun();
                    if (agent.State != AgentRunState.Disposed && agent.Inbox.HasMail())
                    {
                        agent.RequestWake();
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
