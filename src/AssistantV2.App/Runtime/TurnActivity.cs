namespace AssistantV2.App.Runtime;

public sealed class TurnActivity
{
    public bool DidHandleMail { get; private set; }

    public void MarkMailHandled() => DidHandleMail = true;

    public void Reset() => DidHandleMail = false;
}
