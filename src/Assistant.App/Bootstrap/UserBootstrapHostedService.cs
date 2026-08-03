using Assistant.App.Mail;
using Assistant.App.Persistence;
using Assistant.App.Registry;
using Assistant.App.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Bootstrap;

/// <summary>
/// Registers the User root node and either restores LiteDB state or seeds AgentTemplates[0].
/// </summary>
public sealed class UserBootstrapHostedService(
    NodeRegistry registry,
    AgentBootstrap bootstrap,
    MailService mail,
    IAgentStateStore store,
    SessionCheckpoint checkpoint,
    IOptions<Settings> settings) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (store.HasPersistedNodes())
        {
            await RestoreAsync(cancellationToken);
            return;
        }

        await SeedFreshAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task SeedFreshAsync(CancellationToken cancellationToken)
    {
        var cfg = settings.Value;
        var user = registry.RegisterUser(cfg.UserDescription);

        if (cfg.AgentTemplates is not { Count: > 0 })
        {
            return;
        }

        var template = cfg.AgentTemplates[0];
        if (string.IsNullOrWhiteSpace(template.Name))
        {
            return;
        }

        var child = registry.SpawnChild(
            user,
            template.Name.Trim(),
            template.Description,
            template.Instructions,
            parentRole: cfg.UserDescription);
        await bootstrap.BootstrapAsync(child, cancellationToken);
        checkpoint.SaveEmptySession(child);
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        mail.SetMailSeq(store.LoadMailSeq());

        var nodes = PersistedNodeGraph.PruneOrphans(store, store.LoadAllNodes());
        foreach (var doc in nodes.OrderBy(n => n.Id, StringComparer.Ordinal))
        {
            registry.RestoreNode(doc);
        }

        foreach (var group in store.LoadAllMail().GroupBy(m => m.OwnerNodeId, StringComparer.Ordinal))
        {
            if (!registry.TryGet(new NodeId(group.Key), out var owner))
            {
                continue;
            }

            owner.Inbox.ReplaceAll(
                group.OrderBy(m => m.Timestamp).Select(PersistedMappings.ToMailMessage));
        }

        foreach (var handle in registry.All().Where(h => h.RuntimeKind == NodeRuntimeKind.Llm))
        {
            await bootstrap.BootstrapAsync(handle, cancellationToken);

            var persisted = store.LoadSession(handle.Id.Value);
            if (persisted is not null)
            {
                await SessionCheckpoint.ApplyPersistedSessionAsync(handle, persisted, cancellationToken);
            }

            if (handle.Llm is null)
            {
                continue;
            }

            if (handle.Llm.NeedsResumeTurn || handle.Inbox.HasMail())
            {
                handle.Llm.RequestWake();
            }
        }
    }
}
