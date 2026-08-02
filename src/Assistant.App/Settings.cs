using System;

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

    public string RootName { get; set; } = "Secretary";

    public string RootDescription { get; set; } = "Manager";

    public string RootInstructions { get; set; } =
        "Organize the execution of the user's tasks.";

    public string UserDisplayName { get; set; } = "User";

    public string UserDescription { get; set; } = "Director";

    public string UserMailSubject { get; set; } = "User request";
}
