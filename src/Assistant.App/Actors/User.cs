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
        bool IsSystem = false,
        bool IsThinking = false);

    public static void PublishSystem(IContext context, string content) =>
        context.System.EventStream.Publish(new MessageLog("System", null, content, IsSystem: true));

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
            PublishSystem(context, "Starting agent registry");
            var props = context.System.DI().PropsFor<AgentRegistry.Actor>();
            var pid = context.Spawn(props);
            var payload = new Agent.Metadata(AGENT_NAME, "Manager", AGENT_INSTRUCTIONS, IsMaster: true);
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
            PublishSystem(context, $"Registered master agent '{AGENT_NAME}'");
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

                PublishSystem(context, $"User -> {AGENT_NAME}: {input}");
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
            if (msg.IsThinking)
            {
                var name = msg.From ?? "Unknown";
                return $"[grey]{name} · thinking[/] [grey][[{time:HH:mm:ss}]][/]";
            }

            var from = msg.From ?? "Unknown";
            var to = !string.IsNullOrEmpty(msg.To)
                ? $" -> {msg.To}"
                : string.Empty;

            return $"[cyan]{from}{to}[/] [grey][[{time:HH:mm:ss}]][/]";
        }

        private async Task RenderLog(MessageLog msg)
        {
            await _streamingLock.WaitAsync();
            try
            {
                if (msg.IsSystem)
                {
                    AnsiConsole.MarkupLine(
                        $"[grey][[{DateTime.Now:HH:mm:ss}]] [system] {Markup.Escape(msg.Content ?? string.Empty)}[/]");
                    return;
                }

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
                            AnsiConsole.Write(new Rule($"[grey][[{endTime:HH:mm:ss}]] (took {duration.TotalSeconds:F1}s)[/]").RightJustified());
                        }

                        return;
                    }

                    if (!string.IsNullOrEmpty(msg.Content))
                    {
                        AnsiConsole.Markup(Markup.Escape(msg.Content));
                    }

                    return;
                }

                var staticStartTime = DateTime.Now;

                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule(FormatHeader(msg, staticStartTime)).LeftJustified());
                AnsiConsole.Markup(msg.IsThinking
                    ? $"[grey italic]{Markup.Escape(msg.Content ?? "[EMPTY]")}[/]"
                    : $"[yellow]{Markup.Escape(msg.Content ?? "[EMPTY]")}[/]");
                var staticEndTime = DateTime.Now;
                var staticDuration = staticEndTime - staticStartTime;
                AnsiConsole.WriteLine();
                AnsiConsole.Write(new Rule($"[grey][[{staticEndTime:HH:mm:ss}]] (took {staticDuration.TotalSeconds:F1}s)[/]").RightJustified());
            }
            finally
            {
                _streamingLock.Release();
            }
        }
    }
}
