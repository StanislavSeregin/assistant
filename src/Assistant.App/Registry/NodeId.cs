using System;

namespace Assistant.App.Registry;

public readonly record struct NodeId(string Value) : IEquatable<NodeId>
{
    public static NodeId User { get; } = new("User");

    public bool IsUser => Equals(User);

    public override string ToString() => Value;

    public static implicit operator string(NodeId id) => id.Value;
}
