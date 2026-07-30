using System;

namespace Assistant.App.Actors;

internal sealed class AgentTransaction(AgentMessages.InboundMessage incoming, AgentRuntime runtime)
{
    private int _listWhileInProgress;

    public AgentMessages.InboundMessage Incoming { get; } = incoming;

    public string? FinalResponse { get; private set; }

    public bool SentIntermediate { get; private set; }

    public bool HasInProgressChildren => runtime.HasInProgressChildren;

    public bool CanFinishWithoutFinal => HasInProgressChildren;

    /// <summary>Model should stop once a Final reply is staged.</summary>
    public bool ShouldEndModelRun => FinalResponse is not null;

    public void MarkIntermediateSent() => SentIntermediate = true;

    public int NoteListWhileInProgress() =>
        HasInProgressChildren ? ++_listWhileInProgress : _listWhileInProgress;

    public bool TryStageFinal(string response, out string error)
    {
        if (FinalResponse is not null)
        {
            error = "A final response has already been staged.";
            return false;
        }

        if (!runtime.HasActiveParentAssignment)
        {
            error = "No active parent assignment to respond to.";
            return false;
        }

        if (runtime.HasInProgressChildren)
        {
            error = "Subagents are still InProgress. Wait for their Final replies, message them, or dispose them before RespondToParent(Final).";
            return false;
        }

        FinalResponse = response;
        error = string.Empty;
        return true;
    }
}
