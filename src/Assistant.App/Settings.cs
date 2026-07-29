namespace Assistant.App;

public class Settings
{
    public required string ApiKey { get; set; }

    public required string Endpoint { get; set; }

    public required string ModelName { get; set; }

    public int MaxConcurrentAgentRuns { get; set; } = 1;
}
