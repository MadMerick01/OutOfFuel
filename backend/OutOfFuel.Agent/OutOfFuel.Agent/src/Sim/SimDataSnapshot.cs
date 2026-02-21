namespace OutOfFuel.Agent.src.Sim;

public readonly record struct SimDataSnapshot(
    bool Connected,
    bool OnGround,
    double GroundSpeedKts,
    double FuelTotal,
    double FuelPercent);
