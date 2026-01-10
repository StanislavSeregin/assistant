using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using Proto;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assistant.App.Actors;

public static class Agent
{
    public record Init(string Name, string Instructions);

    public record Ask(ChatRole Role, string? Author, string? Content);

    public class Actor(IOptions<Settings> options) : IActor
    {
        private PID? Self { get; set; }

        private ActorSystem? System { get; set; }

        private ChatClientAgent? Agent { get; set; }

        private AgentThread? Thread { get; set; }

        public Task ReceiveAsync(IContext context)
        {
            return context.Message switch
            {
                Init msg => Init(context, msg),
                Ask msg => HandleAsk(context, msg),
                User.Streaming => ForwardToParent(context),
                _ => Task.CompletedTask
            };
        }

        private static Task ForwardToParent(IContext context)
        {
            if (context.Parent is { } pid)
            {
                context.Forward(pid);
            }

            return Task.CompletedTask;
        }

        private async Task Init(IContext context, Init msg)
        {
            Self = context.Self;
            System = context.System;
            Agent = CreateChatClientAgent(msg);
            Thread = Agent.GetNewThread();
        }

        private ChatClientAgent CreateChatClientAgent(Init msg)
        {
            var apiKeyCredential = new ApiKeyCredential(options.Value.ApiKey);
            var openAIClientOptions = new OpenAIClientOptions()
            {
                Endpoint = new Uri(options.Value.Endpoint)
            };

            var openAIClient = new OpenAIClient(apiKeyCredential, openAIClientOptions);
            return openAIClient
                .GetChatClient(options.Value.ModelName)
                .CreateAIAgent(name: msg.Name, instructions: msg.Instructions, tools: [
                    AIFunctionFactory.Create(HiringRequestTool),
                    AIFunctionFactory.Create(GetEmployedStaffTool),
                    AIFunctionFactory.Create(WriteToEmployedStaff)]);
        }

        private Task HandleAsk(IContext context, Ask msg)
        {
            if (Agent is not null && context.Sender is { } pid)
            {
                var chatMessage = new Microsoft.Extensions.AI.ChatMessage(msg.Role, msg.Content)
                {
                    AuthorName = msg.Author
                };

                var stream = Agent.RunStreamingAsync(chatMessage, Thread, cancellationToken: context.CancellationToken);
                var wrappedStream = WrappedResponseStream(stream, Agent.Name, context, pid);
                var payload = new User.Streaming(Agent.Name, wrappedStream);
                var envelope = new MessageEnvelope(payload, context.Self);
                context.Send(pid, envelope);
            }

            return Task.CompletedTask;
        }

        private static async IAsyncEnumerable<string> WrappedResponseStream(
            IAsyncEnumerable<AgentRunResponseUpdate> updates,
            string? author,
            IContext context,
            PID pid)
        {
            var texts = updates
                .Where(update => !string.IsNullOrEmpty(update.Text))
                .Select(update => update.Text);

            var sb = new StringBuilder();
            await foreach (var text in texts)
            {
                sb.Append(text);
                yield return text;
            }

            var payload = new Ask(ChatRole.Assistant, author, sb.ToString());
            var envelope = new MessageEnvelope(payload, context.Self);
            context.Send(pid, envelope);
        }

        [Description("""
        Creates a new job hire request for the HR department to start the recruitment process.
        This includes the job title and a detailed job description for the HR team to use in candidate sourcing and screening.
        """)]
        public void HiringRequestTool(
            [Description("The title of the position to be filled (e.g., 'Senior Software Engineer', 'Marketing Manager').")] string jobTitle,
            [Description("A detailed description of the role, including responsibilities, required skills, qualifications, and any other relevant information for HR.")] string jobDescription)
        {
            // TODO
        }

        [Description("""
        Retrieves the current list of all employed staff members.
        This list typically contains employee identifiers such as name and position.
        """)]
        public void GetEmployedStaffTool()
        {
            // TODO
        }

        [Description("""
        Sends a message to a specific employed staff member.
        The tool identifies the employee by their job title and delivers the provided message.
        """)]
        public void WriteToEmployedStaff(
            [Description("The job title of the employed staff member who should receive the message.")] string jobTitle,
            [Description("The content of the message to be sent to the employee.")] string message)
        {
            // TODO
        }
    }
}
