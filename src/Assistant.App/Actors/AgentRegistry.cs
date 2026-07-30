using Proto;
using Proto.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class AgentRegistry
{
    public record Ready(string Name);

    public record AgentsRequest;

    public record AgentsResponse(Agent.Metadata[] Agents);

    public record Message(string From, string To, string? Content, bool WaitForReply = false);

    public record SendMessageResponse(bool Ok, string Result);

    public record CreateParticipantResponse(bool Ok, string Result);

    public record CancelReplyWait(string From, string To);

    public record ReceivedMessages((string From, string? Content)[] Messages);

    public class Actor : IActor
    {
        private sealed class AgentData(PID pid, Agent.Metadata metadata)
        {
            public PID PID { get; } = pid;

            public Agent.Metadata Metadata { get; } = metadata;

            public List<Message> Inbox { get; } = [];

            public bool IsReady { get; set; }
        }

        private readonly Dictionary<string, AgentData> _agents = new(StringComparer.Ordinal);

        private readonly Dictionary<string, List<Message>> _undelivered = new(StringComparer.Ordinal);

        private readonly Dictionary<(string From, string To), PID> _replyWaiters = [];

        private readonly Dictionary<string, PID> _createWaiters = new(StringComparer.Ordinal);

        private PID? _userPid;

        private bool _userAwaitingPrompt;

        public Task ReceiveAsync(IContext context) =>
            context.Message switch
            {
                Agent.Metadata msg => CreateAgent(context, msg),
                Ready msg => HandleReady(context, msg),
                AgentsRequest => RespondAgents(context),
                Message msg => ReceiveMessage(context, msg),
                CancelReplyWait msg => CancelWait(msg),
                _ => Task.CompletedTask
            };

        private Task CreateAgent(IContext context, Agent.Metadata msg)
        {
            if (_agents.ContainsKey(msg.Name))
            {
                Respond(context, new CreateParticipantResponse(false, $"Participant '{msg.Name}' already exists."));
                return Task.CompletedTask;
            }

            var pid = context.Spawn(context.System.DI().PropsFor<Agent.Actor>());
            var agent = new AgentData(pid, msg);
            _agents.Add(msg.Name, agent);

            if (context.Sender is { } sender)
            {
                _createWaiters[msg.Name] = sender;
            }

            if (_undelivered.Remove(msg.Name, out var pending))
            {
                agent.Inbox.AddRange(pending);
            }

            context.Send(pid, new MessageEnvelope(msg, context.Self));
            return Task.CompletedTask;
        }

        private Task HandleReady(IContext context, Ready msg)
        {
            if (!_agents.TryGetValue(msg.Name, out var agent))
            {
                return Task.CompletedTask;
            }

            if (_createWaiters.Remove(msg.Name, out var createWaiter))
            {
                Reply(context, createWaiter, new CreateParticipantResponse(true, $"Participant '{msg.Name}' is ready."));
            }

            if (agent.Inbox.Count > 0)
            {
                agent.IsReady = false;
                var batch = new ReceivedMessages([.. agent.Inbox.Select(m => (m.From, m.Content))]);
                context.Send(agent.PID, new MessageEnvelope(batch, context.Self));
                agent.Inbox.Clear();
            }
            else
            {
                agent.IsReady = true;
                TryPromptUser(context);
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

        private Task ReceiveMessage(IContext context, Message msg)
        {
            if (msg.From == "User" && context.Sender is { } userPid)
            {
                _userPid = userPid;
                _userAwaitingPrompt = true;
            }

            // Reply fulfills a waitForReply — still ack the sender, otherwise they hold the LLM slot forever.
            if (_replyWaiters.Remove((msg.To, msg.From), out var waiter))
            {
                Reply(context, waiter, new SendMessageResponse(true, msg.Content ?? string.Empty));
                CompleteSend(context, msg, "Sent");
                return Task.CompletedTask;
            }

            if (msg.To == "User")
            {
                Respond(context, new SendMessageResponse(true, "Sent"));
                return Task.CompletedTask;
            }

            if (_agents.TryGetValue(msg.To, out var agent))
            {
                Deliver(context, agent, msg);
                CompleteSend(context, msg, "Sent");
                return Task.CompletedTask;
            }

            if (!_undelivered.TryGetValue(msg.To, out var queue))
            {
                queue = [];
                _undelivered[msg.To] = queue;
            }

            queue.Add(msg);
            CompleteSend(context, msg, $"Queued for '{msg.To}' (participant not created yet).");
            return Task.CompletedTask;
        }

        private Task CancelWait(CancelReplyWait msg)
        {
            _replyWaiters.Remove((msg.From, msg.To));
            return Task.CompletedTask;
        }

        private static void Deliver(IContext context, AgentData agent, Message msg)
        {
            if (agent.IsReady)
            {
                agent.IsReady = false;
                var batch = new ReceivedMessages([(msg.From, msg.Content)]);
                context.Send(agent.PID, new MessageEnvelope(batch, context.Self));
            }
            else
            {
                agent.Inbox.Add(msg);
            }
        }

        private void CompleteSend(IContext context, Message msg, string sentResult)
        {
            if (msg.WaitForReply)
            {
                if (context.Sender is not { } sender)
                {
                    return;
                }

                var key = (msg.From, msg.To);
                if (_replyWaiters.ContainsKey(key))
                {
                    Respond(context, new SendMessageResponse(
                        false,
                        $"Already waiting for a reply from '{msg.To}'."));
                    return;
                }

                _replyWaiters[key] = sender;
                return;
            }

            Respond(context, new SendMessageResponse(true, sentResult));
        }

        private Task RespondAgents(IContext context)
        {
            Respond(context, new AgentsResponse([.. _agents.Values.Select(a => a.Metadata)]));
            return Task.CompletedTask;
        }

        private static void Respond<T>(IContext context, T payload) where T : class
        {
            if (context.Sender is { } pid)
            {
                Reply(context, pid, payload);
            }
        }

        private static void Reply<T>(IContext context, PID pid, T payload) where T : class =>
            context.Send(pid, new MessageEnvelope(payload, context.Self));
    }
}
