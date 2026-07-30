using Assistant.App.Interaction;
using Microsoft.Agents.AI;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal sealed class AgentHistoryCompaction(
    AgentSnapshotCompactor snapshotCompactor,
    IOutputEventSink output,
    bool enabled)
{
    private string _ownerInstructions = string.Empty;
    private string _agentName = string.Empty;
    private int _snapshotVersion;

    public void Bind(string agentName, string ownerInstructions)
    {
        _agentName = agentName;
        _ownerInstructions = ownerInstructions;
    }

    public async Task CompactAfterTurnAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        if (!enabled
            || !session.TryGetInMemoryChatHistory(out var history)
            || history.Count == 0)
        {
            return;
        }

        var nextVersion = checked(_snapshotVersion + 1);
        output.Publish(new CompactionStarted(_agentName, nextVersion, history.Count));

        try
        {
            var operationalState = await BuildOperationalStateAsync(agent, session, cancellationToken);
            var result = await snapshotCompactor.CompactAsync(
                new AgentSnapshotCompactor.Request(
                    _agentName,
                    _ownerInstructions,
                    history,
                    operationalState,
                    _snapshotVersion),
                cancellationToken);

            session.SetInMemoryChatHistory([result.Snapshot]);
            _snapshotVersion = result.Version;

            output.Publish(new CompactionCompleted(
                _agentName,
                result.Version,
                result.Text,
                result.BeforeMessages,
                result.BeforeCharacters,
                result.AfterCharacters,
                result.Duration,
                result.InputTokens,
                result.OutputTokens));
        }
        catch (Exception ex)
        {
            output.Publish(new CompactionFailed(_agentName, ex.Message));
        }
    }

    private static async Task<string> BuildOperationalStateAsync(
        AIAgent agent,
        AgentSession session,
        CancellationToken cancellationToken)
    {
        var todoProvider = agent.GetService<TodoProvider>();
        if (todoProvider is null)
        {
            return "Todos: unavailable.";
        }

        var todos = await todoProvider.GetAllTodosAsync(session, cancellationToken);
        if (todos.Count == 0)
        {
            return "Todos: none.";
        }

        return "Todos:" + Environment.NewLine + string.Join(
            Environment.NewLine,
            todos.Select(todo =>
                $"- [{(todo.IsComplete ? "x" : " ")}] #{todo.Id}: {todo.Title}" +
                (string.IsNullOrWhiteSpace(todo.Description) ? string.Empty : $" — {todo.Description}")));
    }
}
