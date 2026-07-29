using System.Linq;
using Proto;
using Proto.DependencyInjection;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public record MessageLog(
        string? From,
        string? To,
        string? Content,
        Guid? StreamId = null,
        bool IsStreamStart = false,
        bool IsStreamComplete = false,
        bool IsThinking = false,
        bool IsToolCall = false,
        string? ToolName = null,
        bool IsForUser = false,
        long? InputTokens = null);

    public static void PublishToolCall(IContext context, string agent, string toolName, params (string Key, string? Value)[] args)
    {
        var payload = string.Join(
            Environment.NewLine,
            args.Select(arg => $"{arg.Key}: {arg.Value ?? string.Empty}"));

        context.System.EventStream.Publish(new MessageLog(
            agent,
            To: null,
            Content: payload,
            IsToolCall: true,
            ToolName: toolName));
    }

    public record PromptInput;

    public class Actor : IActor
    {
        private const string AGENT_NAME = "Secretary";

        private const string AGENT_INSTRUCTIONS = """
            Coordinate the team. Talk to User directly.
            Delegate specialist work when needed. Be brief.
            """;

        private readonly SemaphoreSlim _streamingLock = new(1);

        private readonly Dictionary<Guid, DateTime> _activeStreams = [];

        private readonly Queue<MessageLog> _deferredMessages = [];

        private readonly List<EventStreamSubscription<object>> _subscriptions = [];

        private PID _registryPid;

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Started => Init(context),
                PromptInput => HandleAsk(context, _registryPid),
                Stopped => Unsubscribe(),
                _ => Task.CompletedTask
            };
        }

        private async Task Init(IContext context)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
            Console.Clear();
            AnsiConsole.Write(new FigletText("Assistant")
            {
                Justification = Justify.Center,
                Color = ConsoleColor.Cyan
            });

            RegisterSubscriptions(context);
            _registryPid = SpawnAgentRegistry(context);
            await HandleAsk(context, _registryPid);
        }

        private void RegisterSubscriptions(IContext context)
        {
            _subscriptions.Add(context.System.EventStream.Subscribe<MessageLog>(RenderLog));
        }

        private Task Unsubscribe()
        {
            foreach (var subscription in _subscriptions)
            {
                subscription.Unsubscribe();
            }

            return Task.CompletedTask;
        }

        private static PID SpawnAgentRegistry(IContext context)
        {
            var props = context.System.DI().PropsFor<AgentRegistry.Actor>();
            var pid = context.Spawn(props);
            var payload = new Agent.Metadata(AGENT_NAME, "Manager", AGENT_INSTRUCTIONS, IsMaster: true);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
            return pid;
        }

        private async Task HandleAsk(IContext context, PID pid)
        {
            await _streamingLock.WaitAsync();
            try
            {
                AnsiConsole.WriteLine();
                var input = AnsiConsole.Prompt(new TextPrompt<string>(">").AllowEmpty());
                if (string.IsNullOrWhiteSpace(input))
                {
                    context.Forward(context.Self);
                    return;
                }

                var payload = new AgentRegistry.Message(From: "User", To: AGENT_NAME, input);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }
            finally
            {
                _streamingLock.Release();
            }
        }

        private static string FormatHeader(MessageLog msg, DateTime time)
        {
            if (msg.IsToolCall)
            {
                var agent = msg.From ?? "Unknown";
                var tool = msg.ToolName ?? "unknown";
                return $"[grey]{agent} · {tool}[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            if (msg.IsThinking)
            {
                var name = msg.From ?? "Unknown";
                return $"[grey]{name} · thinking[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            if (msg.IsForUser || msg.To == "User")
            {
                var from = msg.From ?? "Unknown";
                return $"[bold green]{from} -> You[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            var fromDefault = msg.From ?? "Unknown";
            var to = !string.IsNullOrEmpty(msg.To)
                ? $" -> {msg.To}"
                : string.Empty;

            return $"[cyan]{fromDefault}{to}[/] [grey][[{time:HH:mm:ss}]][/]";
        }

        private static void WriteToolCall(MessageLog msg)
        {
            var time = DateTime.Now;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(FormatHeader(msg, time)).LeftJustified());
            if (!string.IsNullOrEmpty(msg.Content))
            {
                foreach (var line in msg.Content.ReplaceLineEndings("\n").Split('\n'))
                {
                    AnsiConsole.MarkupLine($"[grey]{Markup.Escape(line)}[/]");
                }
            }
        }

        private static string FormatFooter(DateTime endTime, TimeSpan duration, MessageLog msg)
        {
            var timing = $"took {duration.TotalSeconds:F1}s";
            var usage = UsageFormatter.Format(msg.InputTokens);
            return string.IsNullOrEmpty(usage)
                ? $"[grey][[{endTime:HH:mm:ss}]] ({timing})[/]"
                : $"[grey][[{endTime:HH:mm:ss}]] ({timing}. {usage})[/]";
        }

        private async Task RenderLog(MessageLog msg)
        {
            await _streamingLock.WaitAsync();
            try
            {
                if (msg.StreamId is { } streamId)
                {
                    if (msg.IsStreamStart)
                    {
                        var startTime = DateTime.Now;
                        _activeStreams[streamId] = startTime;

                        AnsiConsole.WriteLine();
                        AnsiConsole.Write(new Rule(FormatHeader(msg, startTime)).LeftJustified());
                        AnsiConsole.Markup(msg.IsThinking ? "[grey italic]" : "[yellow]");
                        return;
                    }

                    if (msg.IsStreamComplete)
                    {
                        if (_activeStreams.Remove(streamId, out var startTime))
                        {
                            var endTime = DateTime.Now;
                            var duration = endTime - startTime;
                            AnsiConsole.Reset();
                            AnsiConsole.WriteLine();
                            AnsiConsole.Write(new Rule(FormatFooter(endTime, duration, msg)).RightJustified());
                        }

                        FlushDeferredMessages();
                        return;
                    }

                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        AnsiConsole.Markup(Markup.Escape(msg.Content));
                    }

                    return;
                }

                // Deferred until the active thinking stream finishes.
                if (_activeStreams.Count > 0)
                {
                    _deferredMessages.Enqueue(msg);
                    return;
                }

                if (msg.IsToolCall)
                {
                    WriteToolCall(msg);
                    return;
                }

                WriteStaticMessage(msg);
            }
            finally
            {
                _streamingLock.Release();
            }
        }

        private void FlushDeferredMessages()
        {
            while (_deferredMessages.Count > 0 && _activeStreams.Count == 0)
            {
                var msg = _deferredMessages.Dequeue();
                if (msg.IsToolCall)
                {
                    WriteToolCall(msg);
                }
                else
                {
                    WriteStaticMessage(msg);
                }
            }
        }

        private static void WriteStaticMessage(MessageLog msg)
        {
            var staticStartTime = DateTime.Now;

            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(FormatHeader(msg, staticStartTime)).LeftJustified());
            var contentStyle = msg.IsForUser || msg.To == "User"
                ? "[bold white]"
                : msg.IsThinking
                    ? "[grey italic]"
                    : "[yellow]";
            AnsiConsole.Markup($"{contentStyle}{Markup.Escape(msg.Content ?? "[EMPTY]")}[/]");
            var staticEndTime = DateTime.Now;
            var staticDuration = staticEndTime - staticStartTime;
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule(FormatFooter(staticEndTime, staticDuration, msg)).RightJustified());
        }
    }
}
