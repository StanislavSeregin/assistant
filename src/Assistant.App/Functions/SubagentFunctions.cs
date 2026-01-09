using Assistant.App.Actors;
using Proto;
using System.ComponentModel;

namespace Assistant.App.Functions;

public class SubagentFunctions(IContext context)
{
    [Description("Launch a specialized subagent to handle specific tasks")]
    public void RunSubagent(
        [Description("Unique name identifying the subagent")] string name,
        [Description("System instructions defining the subagent's behavior and capabilities")] string instructions,
        [Description("Initial message or task to send to the subagent")] string message)
    {
        var payload = new Coordinator.RunSubagent(name, instructions, message);
        var envelope = new MessageEnvelope(payload, context.Self);
        context.Send(context.Self, envelope);
    }
}
