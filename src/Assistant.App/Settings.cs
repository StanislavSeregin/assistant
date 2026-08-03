using System;
using System.Collections.Generic;

namespace Assistant.App;

public class Settings
{
    public required string ApiKey { get; set; }

    public required string Endpoint { get; set; }

    public required string ModelName { get; set; }

    public int MaxConcurrentAgentRuns { get; set; } = 1;

    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public bool EnableAgentSkills { get; set; } = true;

    public string SkillsPath { get; set; } = "skills";

    /// <summary>Identity presets for user-spawned agents. First (if named) is seeded at startup.</summary>
    public List<AgentTemplate> AgentTemplates { get; set; } = [];

    public string UserDescription { get; set; } = "Director";

    public string UserMailSubject { get; set; } = "User request";

    /// <summary>
    /// Path to the LiteDB state file. Empty = %LocalAppData%/Assistant/state.litedb.
    /// </summary>
    public string StateDbPath { get; set; } = string.Empty;
}
