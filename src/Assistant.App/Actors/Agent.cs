using Assistant.App.Interaction;
using Microsoft.Extensions.Options;
using Proto;
using System;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Metadata(
        string Name,
        string Description,
        string Instructions,
        bool IsMaster,
        string ParentName,
        string? ParentDescription = null);

    public class Actor : IActor
    {
        private readonly ChatClientFactory _chatClientFactory;
        private readonly AgentTurnTimeline _timeline;
        private readonly AgentMessagePublisher _messages;
        private readonly AgentRuntime _runtime;
        private readonly AgentCollaborationTools _tools;
        private readonly AgentTurnRunner _runner;
        private readonly AgentHistoryCompaction _compaction;
        private readonly AgentTurnOrchestrator _orchestrator;
        private readonly Settings _settings;

        private Metadata Metadata
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        public Actor(
            ChatClientFactory chatClientFactory,
            AgentConcurrencyLimiter concurrencyLimiter,
            AgentSnapshotCompactor snapshotCompactor,
            IOutputEventSink output,
            IOptions<Settings> settings)
        {
            _settings = settings.Value;
            _chatClientFactory = chatClientFactory;
            _timeline = new AgentTurnTimeline(output);
            _messages = new AgentMessagePublisher(output, _timeline);
            _runtime = new AgentRuntime();
            _tools = new AgentCollaborationTools(_runtime, _messages, _timeline);
            _runner = new AgentTurnRunner(
                chatClientFactory,
                concurrencyLimiter,
                output,
                _timeline,
                _tools);
            _compaction = new AgentHistoryCompaction(
                snapshotCompactor,
                output,
                _settings.EnableSnapshotCompaction);
            _orchestrator = new AgentTurnOrchestrator(
                output,
                _runtime,
                _messages,
                _tools,
                _runner,
                _compaction);
        }

        public async Task ReceiveAsync(IContext context)
        {
            _runtime.Bind(context);
            await (context.Message switch
            {
                Metadata msg => InitAsync(msg, context),
                AgentMessages.ParentMessage msg => HandleParentMessageAsync(msg),
                AgentMessages.ChildReply msg => HandleChildReplyAsync(msg),
                Terminated msg => HandleTerminated(msg),
                _ => Task.CompletedTask
            });
        }

        private async Task InitAsync(Metadata msg, IContext context)
        {
            Metadata = msg;
            _tools.Metadata = msg;
            _timeline.Bind(msg.Name);
            _runtime.BindAgent(msg.Name, msg.IsMaster, msg.Description, msg.ParentName);
            _runner.Bind(msg.Name);
            _orchestrator.Bind(msg.Name);
            _messages.Bind(msg.Name, () => _tools.Transaction?.Incoming.TransactionId);

            var bootstrapped = await AgentSessionBootstrap.CreateAsync(
                _chatClientFactory,
                msg,
                _settings,
                _runner.InvokeToolAsync,
                context.CancellationToken);

            _runner.Agent = bootstrapped.Agent;
            _runner.Session = bootstrapped.Session;
            _orchestrator.Agent = bootstrapped.Agent;
            _orchestrator.Session = bootstrapped.Session;
            _compaction.Bind(msg.Name, bootstrapped.OwnerInstructions);
        }

        private Task HandleParentMessageAsync(AgentMessages.ParentMessage msg)
        {
            _runtime.NoteParentName(msg.From);
            return _orchestrator.HandleMessageAsync(AgentMessages.InboundMessage.FromParent(msg));
        }

        private Task HandleChildReplyAsync(AgentMessages.ChildReply msg) =>
            _orchestrator.HandleMessageAsync(AgentMessages.InboundMessage.FromChild(msg));

        private Task HandleTerminated(Terminated msg)
        {
            _runtime.OnChildTerminated(msg.Who);
            return Task.CompletedTask;
        }
    }
}
