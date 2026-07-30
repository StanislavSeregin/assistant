using Assistant.App.Interaction;
using Proto;
using Proto.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

internal sealed class AgentRuntime
{
    private sealed class ChildState(PID pid, Agent.Metadata metadata)
    {
        public PID Pid { get; } = pid;
        public Agent.Metadata Metadata { get; } = metadata;
        public bool InProgress { get; set; }
        public string? LastRequestId { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<string, ChildState> _children = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _requestPreviews = new(StringComparer.Ordinal);
    private IContext? _context;
    private CancellationTokenSource? _transactionCancellation;
    private int _requestSeq;
    private string? _activeParentRequestId;
    private string _parentName = "parent";
    private string _agentDescription = string.Empty;

    public string AgentName { get; private set; } = string.Empty;

    public bool IsRoot { get; private set; }

    public void Bind(IContext context) => _context = context;

    public void BindAgent(string agentName, bool isRoot, string description, string parentName)
    {
        AgentName = agentName;
        IsRoot = isRoot;
        _agentDescription = description ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(parentName))
        {
            _parentName = parentName.Trim();
        }
        else if (isRoot)
        {
            _parentName = "User";
        }
    }

    public void NoteParentName(string from)
    {
        if (!string.IsNullOrWhiteSpace(from))
        {
            _parentName = from.Trim();
        }
    }

    public void SetTransactionCancellation(CancellationTokenSource? cancellation) =>
        _transactionCancellation = cancellation;

    public CancellationToken TransactionCancellation
    {
        get
        {
            var context = Context;
            return _transactionCancellation?.Token ?? context.CancellationToken;
        }
    }

    public CancellationToken ContextCancellation => Context.CancellationToken;

    public string? ActiveParentRequestId
    {
        get
        {
            lock (_sync)
            {
                return _activeParentRequestId;
            }
        }
    }

    public bool HasActiveParentAssignment => ActiveParentRequestId is not null;

    public bool HasInProgressChildren
    {
        get
        {
            lock (_sync)
            {
                return _children.Values.Any(child => child.InProgress);
            }
        }
    }

    public void OpenParentAssignment(string requestId, string content)
    {
        lock (_sync)
        {
            _activeParentRequestId = requestId;
        }

        RememberPreview(requestId, content);
    }

    public string NextRequestId()
    {
        lock (_sync)
        {
            _requestSeq++;
            return $"r{_requestSeq}";
        }
    }

    public string SpawnSubagent(
        string name,
        string description,
        string instructions,
        string message,
        AgentMessagePublisher messages)
    {
        var context = Context;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Name is empty.";
        }

        if (string.Equals(name, AgentName, StringComparison.Ordinal)
            || string.Equals(name, "User", StringComparison.Ordinal))
        {
            return $"'{name}' is reserved.";
        }

        var body = message.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Message is empty. Pass the first task in `message`.";
        }

        lock (_sync)
        {
            if (_children.ContainsKey(name))
            {
                return $"Subagent '{name}' already exists. Use {nameof(AgentCollaborationTools.MessageSubagent)} or dispose it first.";
            }
        }

        var metadata = new Agent.Metadata(
            name,
            description.Trim(),
            instructions.Trim(),
            IsMaster: false,
            ParentName: AgentName,
            ParentDescription: string.IsNullOrWhiteSpace(_agentDescription) ? null : _agentDescription);
        var pid = context.Spawn(context.System.DI().PropsFor<Agent.Actor>());
        context.Watch(pid);

        var requestId = NextRequestId();
        RememberPreview(requestId, body);

        lock (_sync)
        {
            _children[name] = new ChildState(pid, metadata)
            {
                InProgress = true,
                LastRequestId = requestId
            };
        }

        context.Send(pid, new MessageEnvelope(metadata, context.Self));
        context.Send(
            pid,
            new MessageEnvelope(
                new AgentMessages.ParentMessage(requestId, AgentName, body),
                context.Self));

        messages.PublishOutbound(name, body, MessageKind.Message, requestId);
        return
            $"Spawned '{name}', requestId={requestId}. " +
            "They will report back on their own as an incoming message. " +
            "After you finish assigning work this turn, prefer to stop " +
            "(optional RespondToParent Intermediate, then end). Avoid micromanaging or wait-looping ListSubagents.";
    }

    public string MessageSubagent(string name, string message, AgentMessagePublisher messages)
    {
        var context = Context;
        name = name.Trim();
        var body = message.Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Message is empty. Pass the recipient-facing text in `message`.";
        }

        ChildState child;
        lock (_sync)
        {
            if (!_children.TryGetValue(name, out child!))
            {
                return $"Subagent '{name}' not found. Use {nameof(AgentCollaborationTools.ListSubagents)}.";
            }
        }

        var requestId = NextRequestId();
        RememberPreview(requestId, body);

        lock (_sync)
        {
            child.InProgress = true;
            child.LastRequestId = requestId;
        }

        context.Send(
            child.Pid,
            new MessageEnvelope(
                new AgentMessages.ParentMessage(requestId, AgentName, body),
                context.Self));

        messages.PublishOutbound(name, body, MessageKind.Message, requestId);
        return
            $"Sent to '{name}', requestId={requestId}. " +
            "They will report back on their own as an incoming message. " +
            "After you finish assigning work this turn, prefer to stop " +
            "(optional RespondToParent Intermediate, then end). Avoid micromanaging or wait-looping ListSubagents.";
    }

    public string DisposeSubagent(string name)
    {
        var context = Context;
        name = name.Trim();

        ChildState child;
        lock (_sync)
        {
            if (!_children.TryGetValue(name, out child!))
            {
                return $"Subagent '{name}' not found.";
            }

            _children.Remove(name);
        }

        context.Stop(child.Pid);
        return $"Disposed '{name}' and its subtree.";
    }

    public AgentCollaborationTools.SubagentInfo[] ListSubagents()
    {
        lock (_sync)
        {
            return _children.Select(pair => new AgentCollaborationTools.SubagentInfo(
                pair.Key,
                pair.Value.Metadata.Description,
                pair.Value.InProgress ? "InProgress" : "Idle",
                pair.Value.LastRequestId)).ToArray();
        }
    }

    public bool TrySendIntermediate(string content, AgentMessagePublisher messages, out string error)
    {
        string requestId;
        lock (_sync)
        {
            if (_activeParentRequestId is null)
            {
                error = "No active parent assignment to respond to.";
                return false;
            }

            requestId = _activeParentRequestId;
        }

        DeliverToParent(
            new AgentMessages.ChildReply(
                requestId,
                AgentName,
                ReplyKind.Intermediate,
                content,
                GetPreview(requestId)),
            messages,
            MessageKind.Intermediate);
        error = string.Empty;
        return true;
    }

    public async Task<bool> TryDeliverFinalAsync(
        string content,
        AgentMessagePublisher messages,
        CancellationToken cancellationToken = default)
    {
        string requestId;
        lock (_sync)
        {
            if (_activeParentRequestId is null)
            {
                return false;
            }

            requestId = _activeParentRequestId;
            _activeParentRequestId = null;
        }

        var reply = new AgentMessages.ChildReply(
            requestId,
            AgentName,
            ReplyKind.Final,
            content,
            GetPreview(requestId));

        var context = Context;
        var parent = context.Parent ?? throw new InvalidOperationException("Agent has no parent.");
        messages.PublishOutbound(_parentName, reply.Content, MessageKind.Final, reply.RequestId);

        // Root → User: render the Final before the console prompt races for the gate.
        if (IsRoot)
        {
            await messages.DrainAsync(cancellationToken);
        }

        context.Send(parent, new MessageEnvelope(reply, context.Self));
        return true;
    }

    public void OnChildReply(AgentMessages.ChildReply reply)
    {
        lock (_sync)
        {
            if (!_children.TryGetValue(reply.FromChild, out var child))
            {
                return;
            }

            if (reply.Kind == ReplyKind.Final)
            {
                child.InProgress = false;
            }
        }
    }

    public void OnChildTerminated(PID pid)
    {
        lock (_sync)
        {
            var name = _children.FirstOrDefault(pair => pair.Value.Pid.Equals(pid)).Key;
            if (name is null)
            {
                return;
            }

            _children.Remove(name);
        }
    }

    private void DeliverToParent(
        AgentMessages.ChildReply reply,
        AgentMessagePublisher messages,
        MessageKind kind)
    {
        var context = Context;
        var parent = context.Parent ?? throw new InvalidOperationException("Agent has no parent.");
        messages.PublishOutbound(_parentName, reply.Content, kind, reply.RequestId);
        context.Send(parent, new MessageEnvelope(reply, context.Self));
    }

    private void RememberPreview(string requestId, string body)
    {
        var preview = body.Length <= 120 ? body : body[..117] + "...";
        lock (_sync)
        {
            _requestPreviews[requestId] = preview;
        }
    }

    private string? GetPreview(string requestId)
    {
        lock (_sync)
        {
            return _requestPreviews.TryGetValue(requestId, out var preview) ? preview : null;
        }
    }

    private IContext Context => _context ?? throw new InvalidOperationException();
}
