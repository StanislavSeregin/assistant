using Assistant.App.Interaction;
using Microsoft.Extensions.AI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace Assistant.App.Actors;

internal sealed class AgentCollaborationTools(
    AgentRuntime runtime,
    AgentMessagePublisher messages,
    AgentTurnTimeline timeline)
{
    public static readonly HashSet<string> ApplicationToolNames =
    [
        nameof(SpawnSubagent),
        nameof(MessageSubagent),
        nameof(DisposeSubagent),
        nameof(ListSubagents),
        nameof(RespondToParent)
    ];

    public record SubagentInfo(string Name, string Description, string Status, string? LastRequestId);

    public Agent.Metadata Metadata
    {
        get => field ?? throw new InvalidOperationException();
        set;
    }

    public AgentTransaction? Transaction { get; set; }

    public AITool[] BuildTools(bool canRespond)
    {
        List<AITool> tools =
        [
            AIFunctionFactory.Create(SpawnSubagent),
            AIFunctionFactory.Create(MessageSubagent),
            AIFunctionFactory.Create(DisposeSubagent),
            AIFunctionFactory.Create(ListSubagents)
        ];

        if (canRespond)
        {
            tools.Add(AIFunctionFactory.Create(RespondToParent));
        }

        return [.. tools];
    }

    [Description(
        "Spawn a direct subagent and send its first task. " +
        "Load skill `subagent-management` first. " +
        "description = stable role/duty (NOT the task); instructions = standing working style; message = the concrete task. " +
        "Does not wait for a reply — they report back later. After assigning everyone you need, prefer to end the turn.")]
    public string SpawnSubagent(
        [Description("Unique name among your subagents (e.g. Researcher, Coder)")] string name,
        [Description("Stable role/specialty of this subagent — who they are across tasks, NOT the current assignment")] string description,
        [Description("Standing working style and constraints — not the one-off task (put the task in message)")] string instructions,
        [Description("The concrete first task for this subagent")] string message)
    {
        timeline.CompleteThinking();
        return runtime.SpawnSubagent(name, description, instructions, message, messages);
    }

    [Description(
        "Send a follow-up or new task to one of your direct subagents. Does not wait for a reply — they report back later. " +
        "After assigning everyone you need, prefer to end the turn.")]
    public string MessageSubagent(
        [Description("Name of your direct subagent")] string name,
        [Description("Message content for the subagent")] string message)
    {
        timeline.CompleteThinking();
        return runtime.MessageSubagent(name, message, messages);
    }

    [Description("Stop a direct subagent and its entire subtree.")]
    public string DisposeSubagent(
        [Description("Name of your direct subagent")] string name)
    {
        timeline.CompleteThinking();
        return runtime.DisposeSubagent(name);
    }

    [Description(
        "Snapshot of your direct subagents (InProgress/Idle). " +
        "Prefer checking on a later turn after they report; do not wait-loop right after spawn.")]
    public string ListSubagents()
    {
        var list = runtime.ListSubagents();
        if (list.Length == 0)
        {
            return "No direct subagents.";
        }

        var sb = new StringBuilder();
        foreach (var item in list)
        {
            sb.AppendLine(
                $"- {item.Name}: {item.Status}" +
                (string.IsNullOrEmpty(item.LastRequestId) ? string.Empty : $", lastRequestId={item.LastRequestId}") +
                $" — {item.Description}");
        }

        var transaction = Transaction;
        if (transaction is not null && runtime.HasInProgressChildren)
        {
            var polls = transaction.NoteListWhileInProgress();
            sb.AppendLine();
            sb.Append(
                "Some children are still InProgress — they will report back themselves. " +
                "Prefer ending this turn rather than waiting here. " +
                "Check again on a later turn only if you still need a roster.");
            if (polls >= 2)
            {
                sb.Append(" Repeated checks in the same turn usually mean you should stop and trust the handoff.");
            }
        }

        return sb.ToString().TrimEnd();
    }

    [Description(
        "Reply to your parent. kind=Intermediate for progress/questions (assignment stays open); " +
        "kind=Final to complete the assignment (blocked while any direct subagent is InProgress). " +
        "You may Intermediate and still spawn/message more in the same turn; when the batch is done, prefer to stop and let children report back.")]
    public string RespondToParent(
        [Description("Intermediate or Final")] string kind,
        [Description("Exact parent-facing payload; do not include internal reasoning")] string content)
    {
        var body = content.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Response is empty. Put the complete answer in `content`.";
        }

        if (!Enum.TryParse<ReplyKind>(kind, ignoreCase: true, out var replyKind))
        {
            return "kind must be Intermediate or Final.";
        }

        var transaction = Transaction;
        if (transaction is null)
        {
            return "No active transaction.";
        }

        timeline.CompleteThinking();

        if (replyKind == ReplyKind.Intermediate)
        {
            if (transaction.FinalResponse is not null)
            {
                return "A final response is already staged; intermediate replies are no longer allowed.";
            }

            if (!runtime.TrySendIntermediate(body, messages, out var error))
            {
                return error;
            }

            transaction.MarkIntermediateSent();
            return runtime.HasInProgressChildren
                ? "Intermediate reply sent. When your assignment batch is done, prefer to stop and let InProgress children report back on their own."
                : "Intermediate reply sent to parent.";
        }

        return transaction.TryStageFinal(body, out var stageError)
            ? "Final response staged. Further thinking will be ignored; it will be delivered to the parent shortly."
            : stageError;
    }
}
