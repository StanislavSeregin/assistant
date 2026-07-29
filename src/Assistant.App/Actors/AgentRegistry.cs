using Proto;
using Proto.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class AgentRegistry
{
    public record Ready(string Name);

    public record AgentsRequest;

    public record AgentsResponse(Agent.Metadata[] Agents);

    public record Message(string From, string To, string? Content);

    public record ReceivedMessages((string From, string? Content)[] Messages);

    public class Actor : IActor
    {
        private class AgentData(PID pid, Agent.Metadata metadata)
        {
            public PID PID => pid;

            public Agent.Metadata Metadata => metadata;

            public List<Message> Messages { get; } = [];

            public bool IsReady { get; set; }
        }

        private readonly Dictionary<string, AgentData> _agents = [];

        private readonly List<(string From, string To)> _responseAwaters = [];

        private PID? _userPid;

        private bool _userAwaitingPrompt;

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Agent.Metadata msg => CreateAgent(context, msg),
                Ready msg => HandleReady(context, msg),
                AgentsRequest => RespondAgents(context),
                Message msg => RecieveMessage(context, msg),
                _ => Task.CompletedTask
            };
        }

        private Task CreateAgent(IContext context, Agent.Metadata msg)
        {
            if (_agents.ContainsKey(msg.Name) is false)
            {
                var props = context.System.DI().PropsFor<Agent.Actor>();
                var pid = context.Spawn(props);
                _agents.Add(msg.Name, new AgentData(pid, msg));
                var envelope = new MessageEnvelope(msg, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private Task HandleReady(IContext context, Ready msg)
        {
            if (_agents.TryGetValue(msg.Name, out var agent))
            {
                if (agent.Messages.Count > 0)
                {
                    agent.IsReady = false;
                    var payload = new ReceivedMessages([.. agent.Messages.Select(item => (item.From, item.Content))]);
                    var envelope = new MessageEnvelope(payload, context.Self);
                    context.Send(agent.PID, envelope);
                    agent.Messages.Clear();
                }
                else
                {
                    agent.IsReady = true;
                    TryPromptUser(context);
                }
            }

            return Task.CompletedTask;
        }

        private void TryPromptUser(IContext context)
        {
            if (_userAwaitingPrompt
                && _userPid is { } userPid
                && _agents.Values.All(a => a.IsReady))
            {
                _userAwaitingPrompt = false;
                context.Send(userPid, new User.PromptInput());
            }
        }

        private Task RecieveMessage(IContext context, Message msg)
        {
            if (msg.To == "User")
            {
                return Task.CompletedTask;
            }

            if (msg.From == "User" && context.Sender is { } userPid)
            {
                _userPid = userPid;
                _userAwaitingPrompt = true;
            }

            if (_agents.TryGetValue(msg.To, out var agent))
            {
                TrackResponseAwater(msg.From, msg.To);
                if (agent.IsReady)
                {
                    agent.IsReady = false;
                    var payload = new ReceivedMessages([(msg.From, msg.Content)]);
                    var envelope = new MessageEnvelope(payload, context.Self);
                    context.Send(agent.PID, envelope);
                }
                else
                {
                    agent.Messages.Add(msg);
                }
            }

            return Task.CompletedTask;
        }

        private void TrackResponseAwater(string from, string to)
        {
            if (_responseAwaters.RemoveAll(item => item == (to, from)) is 0)
            {
                _responseAwaters.Add((from, to));
            }
        }

        private Task RespondAgents(IContext context)
        {
            if (context.Sender is { } pid)
            {
                var payload = new AgentsResponse([.. _agents.Values.Select(x => x.Metadata)]);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }
    }
}
