namespace OutOfFuel.Agent.src.Models;

public sealed class AppState
{
    public string Version { get; set; } = "unknown";
    public bool Connected { get; set; }
    public string State { get; set; } = "SAFE";
    public int TimeToCutSec { get; set; } = 900;
    public int IntervalSec { get; set; } = 900;
    public int WarningSec { get; set; } = 90;
    public int RefuelPercent { get; set; } = 40;
    public int FuelRampDownSec { get; set; } = 30;
    public bool OnGround { get; set; }
    public double GroundSpeedKts { get; set; }
    public double FuelPercent { get; set; } = 50;
    public bool RefuelAllowed { get; set; }
    public int StopHoldProgress { get; set; }
    public int LastRefuelSecAgo { get; set; }
}
