namespace Assistant.App.Runtime;

public sealed class TurnActivity
{
    public bool DidHandleMail { get; private set; }

    public bool DidMutateChecklist { get; private set; }

    public bool DidCommitContext { get; private set; }

    /// <summary>
    /// Progress this episode: mail-act or a real checklist mutation.
    /// </summary>
    public bool DidMakeProgress => DidHandleMail || DidMutateChecklist;

    /// <summary>
    /// Commit is allowed as soon as progress succeeded this episode — including in the same
    /// model run, before the runner formally enters the compact phase.
    /// </summary>
    public bool AllowContextCommit => DidMakeProgress;

    public void MarkMailHandled() => DidHandleMail = true;

    public void MarkChecklistMutated() => DidMutateChecklist = true;

    public void MarkContextCommitted() => DidCommitContext = true;

    /// <summary>Clear episode flags so the same lease can run another wake after CommitContext.</summary>
    public void ResetForContinuation()
    {
        DidHandleMail = false;
        DidMutateChecklist = false;
        DidCommitContext = false;
    }

    public static TurnActivity FromPersisted(
        bool didHandleMail,
        bool didCommitContext,
        bool didMutateChecklist = false)
    {
        var activity = new TurnActivity();
        if (didHandleMail)
        {
            activity.MarkMailHandled();
        }

        if (didMutateChecklist)
        {
            activity.MarkChecklistMutated();
        }

        if (didCommitContext)
        {
            activity.MarkContextCommitted();
        }

        return activity;
    }
}
