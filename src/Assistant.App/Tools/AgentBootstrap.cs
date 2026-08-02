using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Support;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Tools;

public sealed class AgentBootstrap(
    ChatClientFactory chatClientFactory,
    IOptions<Settings> settings)
{
    public void Bootstrap(NodeHandle handle)
    {
        BootstrapAsync(handle, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task BootstrapAsync(NodeHandle handle, CancellationToken cancellationToken)
    {
        var llm = handle.Llm
            ?? throw new InvalidOperationException($"Node '{handle.Name}' is not an LLM node.");

        if (llm.Agent is not null)
        {
            return;
        }

        var cfg = settings.Value;
        var instructions = BuildInstructions(handle);

        var injecting = new MailNoticeInjectingChatClient(
            chatClientFactory.GetChatClient(),
            handle);

        var agent = injecting.AsHarnessAgent(new HarnessAgentOptions
        {
            Name = handle.Name,
            Description = handle.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions
            },
            AIContextProviders = CreateSkillsProviders(cfg),
            DisableAgentModeProvider = true,
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableAgentSkillsProvider = true,
            DisableOpenTelemetry = true
        });

        var session = await agent.CreateSessionAsync(cancellationToken);
        llm.BindSession(agent, session);
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

    public static string BuildInstructions(NodeHandle handle)
    {
        var roleBlock = string.IsNullOrWhiteSpace(handle.Instructions)
            ? string.Empty
            : $"""


                {handle.Instructions.Trim()}
                """;

        var parentName = handle.ParentId?.Value ?? NodeId.User.Value;
        var parentLabel = string.IsNullOrWhiteSpace(handle.ParentDescription)
            ? parentName
            : $"{parentName} ({handle.ParentDescription.Trim()})";

        return $"""
            You are {handle.Name}. {handle.Description}.{roleBlock}

            Reporting line:
            - Your parent / manager is {parentLabel}.
            - You are subordinate only to them. Mail them with WriteMail / ReplyMail.

            Your job is to solve the ask and help your parent.

            Channel:
            - Only WriteMail / ReplyMail are delivered. Free text and thinking are private.
            - ReplyMail answers an inbox mail; WriteMail starts a new conversation.
            - You can mail your parent and your direct subagents (GetRecipients).
            - Messages prefixed [SYSTEM] are runtime notices (wake, continuity, gentle
              reminders). They are not inbox mail — do not ReplyMail them.

            Each wake:
            - Handle mail first.
            - {ContinuityHandoffGuide.BootstrapBlurb}
            - Before any subagent collaboration, load `subagent-management`.
            - After spawning, WriteMail the subagent to brief them.
            - DisposeSubagent when a child's work is finished.

            Be brief; prefer tools over deliberation.
            """;
    }
}
