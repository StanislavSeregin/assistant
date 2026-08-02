using Assistant.App.Registry;
using Assistant.App.Runtime;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Tools;

public sealed class AgentBootstrap(
    ChatClientFactory chatClientFactory,
    IOptions<Settings> settings,
    IServiceProvider services)
{
    public void Bootstrap(AgentHandle handle)
    {
        BootstrapAsync(handle, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task BootstrapAsync(AgentHandle handle, CancellationToken cancellationToken)
    {
        if (handle.Agent is not null)
        {
            return;
        }

        var turnRunner = services.GetRequiredService<TurnRunner>();
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

        if (agent.GetService<FunctionInvokingChatClient>() is { } functionClient)
        {
            functionClient.FunctionInvoker = turnRunner.InvokeToolAsync;
        }

        var session = await agent.CreateSessionAsync(cancellationToken);
        handle.BindSession(agent, session);
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

    public static string BuildInstructions(AgentHandle handle)
    {
        var roleBlock = string.IsNullOrWhiteSpace(handle.Instructions)
            ? string.Empty
            : $"""


                {handle.Instructions.Trim()}
                """;

        var parentLabel = string.IsNullOrWhiteSpace(handle.ParentDescription)
            ? handle.ParentId.Value
            : $"{handle.ParentId.Value} ({handle.ParentDescription.Trim()})";

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
            - Messages prefixed [SYSTEM] are runtime turn notices (wake, channel corrections).
              They are not inbox mail and not from a person — do not ReplyMail them.
            - Before any subagent collaboration, load `subagent-management`.
            - After spawning, WriteMail the subagent to brief them.
            - DisposeSubagent when a child's work is finished.
            - After you have delivered what this turn needs, you are done.

            Be brief; prefer tools over deliberation.
            """;
    }
}
