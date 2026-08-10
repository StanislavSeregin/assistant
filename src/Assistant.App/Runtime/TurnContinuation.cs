using Assistant.App.Registry;
using System.Linq;

namespace Assistant.App.Runtime;

/// <summary>Shared predicates for turn progress / continue-in-slot / release.</summary>
public static class TurnContinuation
{
    public static bool HasOpenChecklist(NodeHandle agent) =>
        agent.Llm?.Checklist.HasOpenItems == true;

    public static bool HasParentMail(NodeHandle agent) =>
        agent.Inbox.List().Any(static m => m.IsFromParent);

    public static bool ShouldContinueInSlot(NodeHandle agent) =>
        HasOpenChecklist(agent) || HasParentMail(agent);

    public static bool CanRelease(NodeHandle agent) => !ShouldContinueInSlot(agent);
}
