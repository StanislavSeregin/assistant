namespace Assistant.App;

/// <summary>
/// User-configured agent identity preset. First entry in Settings.AgentTemplates
/// is spawned at startup when present; empty list means no default agent.
/// </summary>
public sealed class AgentTemplate
{
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public string Instructions { get; set; } = "";
}
