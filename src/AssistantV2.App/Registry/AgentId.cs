using System;

namespace AssistantV2.App.Registry;

public readonly record struct AgentId(string Value) : IEquatable<AgentId>
{
    public static AgentId User { get; } = new("User");

    public bool IsUser => Equals(User);

    public override string ToString() => Value;

    public static implicit operator string(AgentId id) => id.Value;
}
