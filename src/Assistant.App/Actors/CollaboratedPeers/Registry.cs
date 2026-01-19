using Proto;
using Proto.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors.CollaboratedPeers;

public static class Registry
{
    public record Metadata(string Name, string Description, string? Instructions);

    public record Init(Metadata Parent, Metadata Master);

    public record InitAgent(Metadata Metadata);

    public record Message(string To, string? Content);

    public record Envelope(string From, Message Message, Metadata[] AvailableParticipants)
    {
        public string GetStructuredText()
        {
            return $"""
            == GOT MESSAGE ==
            From: {From}
            Available participants:
            {string.Join(Environment.NewLine, AvailableParticipants.Select(m => $"- [{m.Name}]: {m.Description}"))}
            ---

            {Message.Content ?? "EMPTY"}
            """;
        }
    }

    public class Actor : IActor
    {
        private readonly Dictionary<PID, Metadata> _metaByPidDict = [];

        private readonly Dictionary<string, PID> _pidByNameDict = [];

        private Metadata Parent
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        private PID Master
        {
            get => field ?? throw new InvalidOperationException();
            set;
        }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Init msg => Init(context, msg),
                InitAgent msg => InitAgent(context, msg),
                Message msg => ForwardMessage(context, msg),
                _ => Task.CompletedTask
            };
        }

        private Task Init(IContext context, Init msg)
        {
            Parent = msg.Parent;
            Master = InitAgentInternal(context, msg.Master);
            return Task.CompletedTask;
        }

        private Task InitAgent(IContext context, InitAgent msg)
        {
            InitAgentInternal(context, msg.Metadata);
            return Task.CompletedTask;
        }

        private PID InitAgentInternal(IContext context, Metadata metadata)
        {
            if (_pidByNameDict.ContainsKey(metadata.Name))
            {
                throw new InvalidOperationException($"Agent already exists: {metadata.Name}");
            }

            var props = context.System.DI().PropsFor<Agent.Actor>();
            var pid = context.Spawn(props);
            _metaByPidDict.Add(pid, metadata);
            _pidByNameDict.Add(metadata.Name, pid);
            var payload = new InitAgent(metadata);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(Master, envelope);
            return pid;
        }

        private Task ForwardMessage(IContext context, Message msg)
        {
            if (GetFrom(context) is { } from && GetTo(context, msg.To) is { } to)
            {
                var availableParticipants = GetAvailableParticipants(to).ToArray();
                var payload = new Envelope(from, msg, availableParticipants);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(to, envelope);
            }

            return Task.CompletedTask;
        }

        private string? GetFrom(IContext context)
        {
            if (context.Sender is null)
            {
                return null;
            }
            else if (context.Sender == context.Parent)
            {
                return Parent.Name;
            }
            else if (_metaByPidDict.TryGetValue(context.Sender, out var meta))
            {
                return meta.Name;
            }

            return null;
        }

        private PID? GetTo(IContext context, string to)
        {
            if (Parent.Name == to && context.Parent is { } parent)
            {
                return parent;
            }
            else if (_pidByNameDict.TryGetValue(to, out var targetPid))
            {
                return targetPid;
            }

            return null;
        }

        private IEnumerable<Metadata> GetAvailableParticipants(PID target)
        {
            if (target == Master)
            {
                yield return Parent;
            }

            foreach (var meta in _metaByPidDict.Where(kvp => kvp.Key != target).Select(kvp => kvp.Value))
            {
                yield return meta;
            }
        }
    }
}
