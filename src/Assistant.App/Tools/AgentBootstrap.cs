using Assistant.App.Lifecycle;
using Assistant.App.Registry;
using Assistant.App.Runtime;
using Assistant.App.Support;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable MAAI001 // FileAccessProvider / AgentFileStore are evaluation-only APIs.

namespace Assistant.App.Tools;

public sealed class AgentBootstrap(
    ChatClientFactory chatClientFactory,
    IOptions<Settings> settings,
    ILifecycleSink lifecycle)
{
    private readonly object _fileAccessGate = new();
    private AgentFileStore? _sharedFileAccessStore;
    private string? _sharedFileAccessRoot;
    private LocalShellExecutor? _sharedShell;
    private AITool? _sharedShellTool;

    /// <summary>
    /// Auto-approved <c>run_shell</c> tool scoped to <see cref="Settings.AgentFileAccessPath"/>, or null when disabled.
    /// </summary>
    public AITool? ShellTool => _sharedShellTool;

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
        var fileAccessStore = GetOrCreateFileAccessStore(cfg);
        var shell = GetOrCreateShell(cfg);
        var shellEnabled = shell is not null;
        var instructions = BuildInstructions(
            handle,
            fileAccessEnabled: fileAccessStore is not null,
            shellEnabled: shellEnabled);

        var injecting = new SystemNotificationInjectingChatClient(
            chatClientFactory.GetChatClient(),
            handle,
            lifecycle);

        var agent = injecting.AsHarnessAgent(new HarnessAgentOptions
        {
            Name = handle.Name,
            Description = handle.Description,
            ChatOptions = new ChatOptions
            {
                Instructions = instructions
            },
            AIContextProviders = CreateContextProviders(cfg, shell),
            FileAccessStore = fileAccessStore,
            FileAccessProviderOptions = fileAccessStore is null
                ? null
                : new FileAccessProviderOptions
                {
                    DisableReadOnlyToolApproval = true,
                    DisableWriteToolApproval = true
                },
            DisableAgentModeProvider = true,
            DisableWebSearch = true,
            DisableFileMemory = true,
            DisableAgentSkillsProvider = true,
            DisableOpenTelemetry = true
        });

        var session = await agent.CreateSessionAsync(cancellationToken);
        llm.BindSession(agent, session);
    }

    private AgentFileStore? GetOrCreateFileAccessStore(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentFileAccessPath))
        {
            return null;
        }

        var root = Path.GetFullPath(settings.AgentFileAccessPath);

        lock (_fileAccessGate)
        {
            if (_sharedFileAccessStore is not null
                && string.Equals(_sharedFileAccessRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return _sharedFileAccessStore;
            }

            Directory.CreateDirectory(root);
            _sharedFileAccessRoot = root;
            _sharedFileAccessStore = new FileSystemAgentFileStore(root);
            return _sharedFileAccessStore;
        }
    }

    private LocalShellExecutor? GetOrCreateShell(Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.AgentFileAccessPath))
        {
            return null;
        }

        var root = Path.GetFullPath(settings.AgentFileAccessPath);

        lock (_fileAccessGate)
        {
            if (_sharedShell is not null
                && string.Equals(_sharedFileAccessRoot, root, StringComparison.OrdinalIgnoreCase))
            {
                return _sharedShell;
            }

            Directory.CreateDirectory(root);
            _sharedFileAccessRoot = root;
            _sharedShell = new LocalShellExecutor(new LocalShellExecutorOptions
            {
                Mode = ShellMode.Stateless,
                WorkingDirectory = root,
                ConfineWorkingDirectory = true,
                AcknowledgeUnsafe = true,
                Timeout = TimeSpan.FromSeconds(30),
                Policy = new ShellPolicy(denyList:
                [
                    @"\brm\s+-rf\b",
                    @"\bsudo\b",
                    @":\(\)\s*\{",
                    @"\bmkfs\b",
                    @">\s*/dev/sd",
                    @"\bFormat-Volume\b",
                    @"\bRemove-Item\s+.*-Recurse\b",
                ]),
            });
            // Harness 1.16 cannot take ShellExecutor; wire the tool ourselves (auto-approved).
            _sharedShellTool = _sharedShell.AsAIFunction(requireApproval: false);
            return _sharedShell;
        }
    }

    private static AIContextProvider[]? CreateContextProviders(Settings settings, ShellExecutor? shell)
    {
        var providers = new List<AIContextProvider>();

        if (settings.EnableAgentSkills)
        {
            var skillsPath = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, settings.SkillsPath));

            providers.Add(
                new AgentSkillsProvider(
                    skillsPath,
                    options: new AgentSkillsProviderOptions
                    {
                        DisableLoadSkillApproval = true,
                        DisableReadSkillResourceApproval = true,
                        DisableRunSkillScriptApproval = true
                    }));
        }

        if (shell is not null)
        {
            providers.Add(new ShellEnvironmentProvider(shell));
        }

        return providers.Count > 0 ? providers.ToArray() : null;
    }

    public static string BuildInstructions(
        NodeHandle handle,
        bool fileAccessEnabled = false,
        bool shellEnabled = false)
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

        var filesBlock = fileAccessEnabled
            ? """


            Files:
            - Use file_access_* tools for the shared working folder (read, write, ls, grep, replace, delete).
            - Paths are relative to that folder; the same folder is shared across agents.
            """
            : string.Empty;

        if (shellEnabled)
        {
            filesBlock += """

            - Use run_shell for rename, move, and other shell work in that same folder (PowerShell on this host).
            """;
        }

        return $"""
            You are {handle.Name}. {handle.Description}.{roleBlock}

            Reporting line:
            - Your parent / manager is {parentLabel}.
            - You are subordinate only to them. Mail them with WriteMail / ReplyMail.

            Your job is to solve the ask and help your parent.

            Standard cycle (every agent, every non-trivial ask):
            1. Think — restate the goal and what “done” looks like; note gaps.
            2. Plan — decompose into subtasks (order, fan-out, risks).
            3. Act or delegate — do small in-specialty work yourself; if a subtask is
               large, cross-cutting, or needs further decomposition, hire a specialist
               and brief them by mail with the context they need, then wait.
            4. Integrate — check results, recurse on remaining work, report to parent by mail.
            Tiny one-step asks may compress the cycle; do not skip it on multi-step work.
            Prefer a crisp plan and tools over silent rumination. Be brief in mail.

            Channel:
            - Only WriteMail / ReplyMail are delivered. Free text and thinking are private.
            - ReplyMail answers an inbox mail; WriteMail starts a new conversation.
            - You can mail your parent and your direct subagents (GetRecipients).
            - Messages prefixed [SYSTEM] are runtime notices (wake, continuity, gentle
              reminders). They are not inbox mail — do not ReplyMail them.{filesBlock}

            Each wake:
            - If an available skill has the same name as you (your role), or your
              instructions name a role skill, call load_skill for it before doing
              anything else. Do this every wake.
            - Then handle mail (run the standard cycle on each ask).
            - {ContinuityHandoffGuide.BootstrapBlurb}
            - Before any subagent collaboration, load `subagent-management`.
            - After spawning, WriteMail the subagent to brief them.
            - DisposeSubagent when a child's work is finished.
            """;
    }
}
