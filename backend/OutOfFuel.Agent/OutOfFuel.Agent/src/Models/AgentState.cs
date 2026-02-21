namespace OutOfFuel.Agent.src.Models;

public sealed class AgentState
{
    public bool IsConnectedToSim { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
