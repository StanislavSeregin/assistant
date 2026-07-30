using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal static class AgentSessionBootstrap
{
    public sealed record Result(AIAgent Agent, AgentSession Session, string OwnerInstructions);

    public static async Task<Result> CreateAsync(
        ChatClientFactory chatClientFactory,
        Agent.Metadata metadata,
        Settings settings,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> functionInvoker,
        CancellationToken cancellationToken)
    {
        var agentInstructions = BuildInstructions(metadata);
        var ownerInstructions =
            $"{HarnessAgent.DefaultInstructions}{Environment.NewLine}{Environment.NewLine}{agentInstructions}";

        var agent = chatClientFactory.GetChatClient().AsHarnessAgent(new HarnessAgentOptions
        {
            Name = metadata.Name,
            Description = metadata.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = agentInstructions
            },
            AIContextProviders = CreateSkillsProviders(settings),
            DisableAgentModeProvider = true,
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableAgentSkillsProvider = true,
            DisableOpenTelemetry = true
        });

        if (agent.GetService<FunctionInvokingChatClient>() is { } functionClient)
        {
            functionClient.FunctionInvoker = functionInvoker;
        }

        var session = await agent.CreateSessionAsync(cancellationToken);
        return new Result(agent, session, ownerInstructions);
    }

    private static AIContextProvider[]? CreateSkillsProviders(Settings settings)
    {
        if (!settings.EnableAgentSkills)
        {
            return null;
        }

        var skillsPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, settings.SkillsPath));

        return
        [
            new AgentSkillsProvider(
                skillsPath,
                options: new AgentSkillsProviderOptions
                {
                    DisableLoadSkillApproval = true,
                    DisableReadSkillResourceApproval = true,
                    DisableRunSkillScriptApproval = true
                })
        ];
    }

    public static string BuildInstructions(Agent.Metadata metadata)
    {
        var roleBlock = string.IsNullOrWhiteSpace(metadata.Instructions)
            ? string.Empty
            : $"""

                {metadata.Instructions.Trim()}
                """;

        var parentLabel = string.IsNullOrWhiteSpace(metadata.ParentDescription)
            ? metadata.ParentName
            : $"{metadata.ParentName} ({metadata.ParentDescription.Trim()})";

        var instructions = $"""
            You are {metadata.Name}. {metadata.Description}.{roleBlock}

            Reporting line:
            - Your parent / manager is {parentLabel}.
            - You are subordinate only to them. {nameof(AgentCollaborationTools.RespondToParent)} Intermediate and Final go to {metadata.ParentName}.
            - Incoming messages labeled from your parent are from {metadata.ParentName}. Address them as your assigner; do not invent other superiors.

            Channel rule (non-negotiable):
            - Free text / thinking is NEVER delivered to anyone.
            - Before any subagent collaboration, load the `subagent-management` skill and follow its briefing rules (description = role, message = task).
            - Spawn work with {nameof(AgentCollaborationTools.SpawnSubagent)}(name, description, instructions, message). Messaging is non-blocking; replies arrive later as incoming messages tagged with requestId.
            - Follow up with {nameof(AgentCollaborationTools.MessageSubagent)}(name, message). You may fan out to several subagents in one turn.
            - After you have spawned or messaged everyone you need this turn, prefer to stop (optional {nameof(AgentCollaborationTools.RespondToParent)} Intermediate, then end). Subagents will write back on their own — do not micromanage or sit waiting for them in the same turn.
            - Inspect direct children with {nameof(AgentCollaborationTools.ListSubagents)} when you need a roster. You cannot see or message grandchildren. Prefer checking status on a later turn after they report, not in a wait-loop right after spawn.
            - Stop a child subtree with {nameof(AgentCollaborationTools.DisposeSubagent)}(name).
            - Reply to your parent with {nameof(AgentCollaborationTools.RespondToParent)}(kind, content):
              - Intermediate — progress, questions, or partial results (assignment stays open). You may still assign more work in the same turn afterward.
              - Final — completes the current parent assignment and ends this turn. Forbidden while any direct subagent is InProgress.
            - End every turn that still owes a parent answer with Final, or keep at least one subagent InProgress and release the turn. Thinking-only = not done.

            Be brief; prefer tools over deliberation.
            """;

        if (!metadata.IsMaster)
        {
            return instructions;
        }

        return instructions + $"""

            You coordinate with User as your parent. Specialists you spawn talk only to you.
            Every User assignment must eventually get RespondToParent(Final).
            """;
    }
}
