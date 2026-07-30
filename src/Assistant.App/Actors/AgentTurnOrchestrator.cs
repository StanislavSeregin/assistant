using Assistant.App.Interaction;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal sealed class AgentTurnOrchestrator(
    IOutputEventSink output,
    AgentRuntime runtime,
    AgentMessagePublisher messages,
    AgentCollaborationTools tools,
    AgentTurnRunner runner,
    AgentHistoryCompaction compaction)
{
    private string _agentName = string.Empty;

    public AIAgent Agent
    {
        get => field ?? throw new InvalidOperationException();
        set;
    }

    public AgentSession Session
    {
        get => field ?? throw new InvalidOperationException();
        set;
    }

    public void Bind(string agentName) => _agentName = agentName;

    public async Task HandleMessageAsync(AgentMessages.InboundMessage msg)
    {
        if (msg.Kind == InboundKind.ParentMessage)
        {
            runtime.OpenParentAssignment(msg.RequestId, msg.Content);
        }
        else if (msg.ReplyKind is { } childKind)
        {
            runtime.OnChildReply(new AgentMessages.ChildReply(
                msg.RequestId,
                msg.From,
                childKind,
                msg.Content,
                msg.InResponseToPreview));
        }

        var transaction = new AgentTransaction(msg, runtime);
        tools.Transaction = transaction;
        using var transactionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            runtime.ContextCancellation);
        runtime.SetTransactionCancellation(transactionCancellation);

        var canRespond = runtime.HasActiveParentAssignment;
        Exception? failure = null;
        try
        {
            var inbound = new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [new TextContent(msg.FormatForPrompt())]
            };

            await runner.RunAsync(inbound, canRespond, runtime.TransactionCancellation);

            if (canRespond && transaction.FinalResponse is null && !transaction.CanFinishWithoutFinal)
            {
                runtime.TransactionCancellation.ThrowIfCancellationRequested();
                var alert =
                    $"[SYSTEM ERROR] You must call {nameof(AgentCollaborationTools.RespondToParent)}(\"Final\", content) " +
                    $"or keep at least one subagent InProgress. Thinking-only is not done.";
                output.Publish(new NudgeOutput(_agentName, alert));
                await runner.RunAsync(
                    new ChatMessage
                    {
                        Role = ChatRole.User,
                        Contents = [new TextContent(alert)]
                    },
                    canRespond: true,
                    runtime.TransactionCancellation);
            }

            if (canRespond
                && transaction.FinalResponse is null
                && !transaction.CanFinishWithoutFinal)
            {
                throw new InvalidOperationException(
                    "The turn ended without a Final reply to the parent and with no InProgress subagents.");
            }

            if (transaction.FinalResponse is not null && runtime.HasInProgressChildren)
            {
                throw new InvalidOperationException(
                    "A Final reply was staged while subagents are still InProgress.");
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            output.Publish(new ErrorOutput(_agentName, $"Error: {ex.Message}"));
        }
        finally
        {
            try
            {
                if (canRespond && runtime.HasActiveParentAssignment)
                {
                    // Prefer a staged Final even if the run ended with a benign error
                    // (e.g. stream dispose after early stop).
                    if (transaction.FinalResponse is { Length: > 0 } staged
                        && !runtime.HasInProgressChildren)
                    {
                        await runtime.TryDeliverFinalAsync(
                            staged,
                            messages,
                            runtime.ContextCancellation);
                    }
                    else if (failure is not null || transaction.FinalResponse is null)
                    {
                        if (!transaction.CanFinishWithoutFinal)
                        {
                            var response = failure is null
                                ? $"Request failed: {nameof(AgentCollaborationTools.RespondToParent)}(Final) was not called."
                                : $"Request failed: {failure.Message}";
                            await runtime.TryDeliverFinalAsync(
                                response,
                                messages,
                                runtime.ContextCancellation);
                        }
                    }
                }

                await compaction.CompactAfterTurnAsync(
                    Agent,
                    Session,
                    runtime.ContextCancellation);
            }
            catch (Exception ex)
            {
                output.Publish(new ErrorOutput(
                    _agentName,
                    $"Transaction completion failed: {ex.Message}"));
            }
            finally
            {
                runtime.SetTransactionCancellation(null);
                tools.Transaction = null;
            }
        }
    }
}
