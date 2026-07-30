using System;

namespace Assistant.App;

public class Settings
{
    public required string ApiKey { get; set; }

    public required string Endpoint { get; set; }

    public required string ModelName { get; set; }

    public int MaxConcurrentAgentRuns { get; set; } = 1;

    public TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromMinutes(20);
}
