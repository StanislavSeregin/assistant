namespace Assistant.App.Runtime;

public sealed class TurnActivity
{
    public bool DidHandleMail { get; private set; }

    public bool DidCommitContext { get; private set; }

    /// <summary>
    /// Commit is allowed as soon as mail work succeeded this turn — including in the same
    /// model run, before the runner formally enters the compact phase.
    /// </summary>
    public bool AllowContextCommit => DidHandleMail;

    public void MarkMailHandled() => DidHandleMail = true;

    public void MarkContextCommitted() => DidCommitContext = true;

    public static TurnActivity FromPersisted(bool didHandleMail, bool didCommitContext)
    {
        var activity = new TurnActivity();
        if (didHandleMail)
        {
            activity.MarkMailHandled();
        }

        if (didCommitContext)
        {
            activity.MarkContextCommitted();
        }

        return activity;
    }
}
