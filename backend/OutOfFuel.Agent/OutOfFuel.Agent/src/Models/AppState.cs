namespace OutOfFuel.Agent.src.Models;

public sealed class AppState
{
    public bool Connected { get; set; }
    public string State { get; set; } = "SAFE";
    public int TimeToCutSec { get; set; } = 900;
    public int IntervalSec { get; set; } = 900;
    public bool OnGround { get; set; }
    public double GroundSpeedKts { get; set; }
    public double FuelPercent { get; set; } = 50;
    public bool RefuelAllowed { get; set; }
    public int LastRefuelSecAgo { get; set; }
}
