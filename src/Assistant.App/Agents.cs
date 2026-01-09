using Microsoft.Agents.AI;
using System.Text.Json;

namespace Assistant.App;

public class ForkedThread
{
    private readonly JsonElement _originalState;
    private readonly AgentThread _current;

    public ForkedThread(AgentThread original, ChatClientAgent agent)
    {
        _originalState = original.Serialize();
        _current = agent.DeserializeThread(_originalState);
    }

    public AgentThread Current => _current;

    public AgentThread Restore(ChatClientAgent agent)
    {
        return agent.DeserializeThread(_originalState);
    }
}
