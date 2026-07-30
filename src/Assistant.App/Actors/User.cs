using Assistant.App.Interaction;
using Proto;
using Proto.DependencyInjection;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class User
{
    public record PromptInput;

    public class Actor(IUserInput userInput) : IActor
    {
        private const string AgentName = "Secretary";

        private PID _rootPid = null!;
        private int _requestSeq;

        public Task ReceiveAsync(IContext context) =>
            context.Message switch
            {
                Started => Init(context),
                PromptInput => HandleAsk(context),
                AgentMessages.ChildReply msg => HandleRootReply(msg, context),
                _ => Task.CompletedTask
            };

        private async Task Init(IContext context)
        {
            _rootPid = context.Spawn(context.System.DI().PropsFor<Agent.Actor>());
            context.Send(_rootPid, new MessageEnvelope(
                new Agent.Metadata(
                    AgentName,
                    "Manager",
                    Instructions: string.Empty,
                    IsMaster: true,
                    ParentName: "User",
                    ParentDescription: "Director"),
                context.Self));
            await HandleAsk(context);
        }

        private async Task HandleAsk(IContext context)
        {
            var input = await userInput.ReadAsync(context.CancellationToken);
            if (string.IsNullOrWhiteSpace(input))
            {
                context.Send(context.Self, new PromptInput());
                return;
            }

            _requestSeq++;
            context.Send(_rootPid, new MessageEnvelope(
                new AgentMessages.ParentMessage($"u{_requestSeq}", "User", input),
                context.Self));
        }

        private Task HandleRootReply(AgentMessages.ChildReply msg, IContext context)
        {
            if (msg.Kind != ReplyKind.Final)
            {
                return Task.CompletedTask;
            }

            return HandleAsk(context);
        }
    }
}
