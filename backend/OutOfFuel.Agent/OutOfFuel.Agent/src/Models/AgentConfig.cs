using System.Text.Json;

namespace OutOfFuel.Agent.src.Models;

public sealed class AgentConfig
{
    public const string FileName = "config.json";

    public static readonly AgentConfig Defaults = new();

    public int IntervalSec { get; set; } = 900;
    public int WarningSec { get; set; } = 90;
    public int RefuelPercent { get; set; } = 40;
    public int RefuelStopSpeedKts { get; set; } = 2;
    public int RefuelStopHoldSec { get; set; } = 5;
    public int FuelRampDownSec { get; set; } = 30;

    public static AgentConfig LoadOrCreate(string appBaseDirectory)
    {
        var configPath = Path.Combine(appBaseDirectory, FileName);

        if (!File.Exists(configPath))
        {
            Save(configPath, Defaults);
            return Clone(Defaults);
        }

        var json = File.ReadAllText(configPath);
        var config = JsonSerializer.Deserialize<AgentConfig>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (config is null)
        {
            throw new InvalidOperationException($"Failed to deserialize configuration file '{configPath}'.");
        }

        config.Validate();
        return config;
    }

    public void Validate()
    {
        ValidateRange(nameof(IntervalSec), IntervalSec, 1, 86_400);
        ValidateRange(nameof(WarningSec), WarningSec, 1, IntervalSec);
        ValidateRange(nameof(RefuelPercent), RefuelPercent, 1, 100);
        ValidateRange(nameof(RefuelStopSpeedKts), RefuelStopSpeedKts, 0, 100);
        ValidateRange(nameof(RefuelStopHoldSec), RefuelStopHoldSec, 0, 600);
        ValidateRange(nameof(FuelRampDownSec), FuelRampDownSec, 0, IntervalSec);
    }

    private static void Save(string configPath, AgentConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };
        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(configPath, json + Environment.NewLine);
    }

    private static AgentConfig Clone(AgentConfig source)
    {
        return new AgentConfig
        {
            IntervalSec = source.IntervalSec,
            WarningSec = source.WarningSec,
            RefuelPercent = source.RefuelPercent,
            RefuelStopSpeedKts = source.RefuelStopSpeedKts,
            RefuelStopHoldSec = source.RefuelStopHoldSec,
            FuelRampDownSec = source.FuelRampDownSec,
        };
    }

    private static void ValidateRange(string name, int value, int minInclusive, int maxInclusive)
    {
        if (value < minInclusive || value > maxInclusive)
        {
            throw new InvalidOperationException($"Configuration value '{name}' must be between {minInclusive} and {maxInclusive}. Actual: {value}.");
        }
    }
}
