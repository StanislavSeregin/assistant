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

            Parent: {parentLabel}. Mail only them and your direct subagents (GetRecipients).

            Runtime (small context, short wakes):
            Hard asks are solved by cutting work into slices and handing slices down.
            Children may cut further. You integrate reports; you do not hold the whole
            tree in one head. Prefer crisp tools and mail over long private monologue.

            On every non-trivial ask:
            1. Restate goal and what “done” looks like. Stay inside that scope — do not
               expand into adjacent topics the ask did not request. Stay in your zone of
               responsibility: do not pre-empt a specialist’s decisions. If the parent’s
               intent or material is thin — mail them ONE sharp clarifying question about
               that, then stop. Questions about another role’s craft belong to that hire
               (or their role skill defaults), not to you as preparatory theater.
            2. Decompose into subtasks (order, fan-out, risks).
            3. Act or hire:
               - Do yourself: single-step work clearly inside your specialty that fits this wake.
               - Hire: a subtask needs its own plan, several steps, another craft, or would
                 crowd out coordinating. Load `subagent-management` first, SpawnSubagent
                 (identity/stance only), WriteMail a declarative brief (goal, material,
                 constraints, done-criteria, what to report — not step-by-step HOW), then
                 stop and wait. Do not load the child’s craft skill yourself.
               Role skills may demand stricter hiring (e.g. managers who never do craft).
            4. Integrate child reports; recurse on what remains; ReplyMail / WriteMail your
               parent with the outcome. DisposeSubagent when a child is finished.

            Child mail — no wasted cycles:
            - Finished report → read, integrate, report upward or dispose. Do NOT ReplyMail
              the child (no thanks, ok, or closing note). Dispose clears their mail.
            - Clarifying question → ReplyMail the answer; keep them; then stop.

            Channel:
            - Only WriteMail / ReplyMail are delivered. Free text is private.
            - ReplyMail answers inbox mail; WriteMail starts a new thread.
            - [SYSTEM] lines are runtime notices — never ReplyMail them.{filesBlock}

            Each wake:
            - load_skill for your role skill if an available skill matches your name, or your
              instructions name one — before other work, every wake.
            - Handle mail with the cycle above.
            - {ContinuityHandoffGuide.BootstrapBlurb}
            """;
    }
}
