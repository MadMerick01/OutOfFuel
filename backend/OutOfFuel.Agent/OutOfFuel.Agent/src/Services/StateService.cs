using System.Reflection;
using OutOfFuel.Agent.src.Models;
using OutOfFuel.Agent.src.Sim;

namespace OutOfFuel.Agent.src.Services;

public sealed class StateService
{
    private readonly bool _debugEnabled;
    private readonly AgentConfig _config;
    private readonly object _sync = new();
    private readonly AppState _state;
    private readonly ISimDataSource _simDataSource;
    private readonly PeriodicTimer _timer;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private DateTimeOffset? _lastRefuelUtc;
    private DateTimeOffset? _stopCandidateSinceUtc;
    private DateTimeOffset? _lastTickUtc;

    private bool _hasCycle;
    private bool _starvingLatched;

    public StateService(bool debugEnabled, AgentConfig config, ISimDataSource simDataSource)
    {
        _debugEnabled = debugEnabled;
        _config = config;
        _simDataSource = simDataSource;
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(1.0 / _config.TickHz));
        _state = new AppState
        {
            Version = ResolveVersion(),
            IntervalSec = _config.IntervalSec,
            WarningSec = _config.WarningSec,
            RefuelPercent = _config.RefuelPercent,
            TimeToCutSec = _config.IntervalSec,
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => RunTimerLoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();

        if (_loopTask is not null)
        {
            await _loopTask;
        }

        _timer.Dispose();
        _simDataSource.Dispose();
        _loopCts.Dispose();
    }

    public AppState GetStateSnapshot()
    {
        lock (_sync)
        {
            return new AppState
            {
                Version = _state.Version,
                Connected = _state.Connected,
                State = _state.State,
                TimeToCutSec = _state.TimeToCutSec,
                IntervalSec = _state.IntervalSec,
                WarningSec = _state.WarningSec,
                RefuelPercent = _state.RefuelPercent,
                OnGround = _state.OnGround,
                GroundSpeedKts = _state.GroundSpeedKts,
                FuelPercent = _state.FuelPercent,
                RefuelAllowed = _state.RefuelAllowed,
                StopHoldProgress = _state.StopHoldProgress,
                LastRefuelSecAgo = _state.LastRefuelSecAgo,
                LeakActive = _state.LeakActive,
                DrainPerSecond = _state.DrainPerSecond,
                StartFuelTotal = _state.StartFuelTotal,
                MinFuelTotal = _state.MinFuelTotal,
                FuelTotal = _state.FuelTotal,
            };
        }
    }

    public bool IsConnected()
    {
        lock (_sync)
        {
            return _state.Connected;
        }
    }

    public (bool Ok, string? Reason) RequestRefuel()
    {
        lock (_sync)
        {
            if (!_state.RefuelAllowed)
            {
                if (_debugEnabled)
                {
                    Console.WriteLine("[DEBUG] Refuel rejected: not allowed");
                }

                return (false, "not allowed");
            }

            var baselineFuel = _hasCycle ? _state.StartFuelTotal : _state.FuelTotal;
            var refuelTargetFromPercent = baselineFuel * (_config.RefuelPercent / 100.0);
            var minimumRefuelTarget = _state.MinFuelTotal + Math.Max(_config.StarveEpsilon * 2, 0.1);
            var targetFuelTotal = SanitizeFuel(Math.Max(refuelTargetFromPercent, minimumRefuelTarget));

            _simDataSource.SetTotalFuel(targetFuelTotal);
            _state.FuelTotal = targetFuelTotal;

            StartNewCycle(targetFuelTotal, "refuel");
            _state.TimeToCutSec = _config.IntervalSec;
            _state.State = "SAFE";
            _starvingLatched = false;
            _lastRefuelUtc = DateTimeOffset.UtcNow;
            _state.LastRefuelSecAgo = 0;

            if (_debugEnabled)
            {
                Console.WriteLine($"[DEBUG] Refuel success: targetFuelTotal={targetFuelTotal:F3}, refuelPercent={_config.RefuelPercent}");
            }

            return (true, null);
        }
    }

    private async Task RunTimerLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(cancellationToken))
            {
                Tick();
            }
        }
        catch (OperationCanceledException)
        {
            // graceful shutdown
        }
    }

    private void Tick()
    {
        var simData = _simDataSource.Poll();
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            var deltaTimeSec = _lastTickUtc.HasValue ? Math.Max((now - _lastTickUtc.Value).TotalSeconds, 0) : 1.0 / _config.TickHz;
            _lastTickUtc = now;

            _state.Connected = simData.Connected;
            _state.OnGround = simData.OnGround;
            _state.GroundSpeedKts = SanitizeGroundSpeed(simData.GroundSpeedKts);
            _state.FuelTotal = SanitizeFuel(simData.FuelTotal);
            _state.FuelPercent = SanitizePercent(simData.FuelPercent);

            if (!_hasCycle && _state.Connected && _state.FuelTotal > 0)
            {
                StartNewCycle(_state.FuelTotal, "initial-read");
            }

            UpdateRefuelGate(now);

            if (!_state.OnGround && _state.TimeToCutSec > 0)
            {
                var nextTime = _state.TimeToCutSec - deltaTimeSec;
                _state.TimeToCutSec = (int)Math.Clamp(Math.Ceiling(nextTime), 0, _config.IntervalSec);
            }

            ApplyLeak(deltaTimeSec);

            if (_lastRefuelUtc.HasValue)
            {
                _state.LastRefuelSecAgo = (int)Math.Max(0, (now - _lastRefuelUtc.Value).TotalSeconds);
            }

            var previousState = _state.State;
            _state.State = ResolveState(previousState);

            if (_debugEnabled && !string.Equals(previousState, _state.State, StringComparison.Ordinal))
            {
                Console.WriteLine($"[DEBUG] State transition: {previousState} -> {_state.State} (timeToCutSec={_state.TimeToCutSec}, fuelTotal={_state.FuelTotal:F3})");
            }
        }
    }

    private void ApplyLeak(double deltaTimeSec)
    {
        if (_starvingLatched)
        {
            _state.LeakActive = false;
            return;
        }

        var leakActive = _state.Connected && !_state.OnGround;
        _state.LeakActive = leakActive;

        if (!leakActive || !_hasCycle)
        {
            return;
        }

        var drainAmount = _state.DrainPerSecond * Math.Max(deltaTimeSec, 0);
        var targetFuel = Math.Clamp(_state.FuelTotal - drainAmount, _state.MinFuelTotal, _state.FuelTotal);

        if (targetFuel < _state.FuelTotal)
        {
            _simDataSource.SetTotalFuel(targetFuel);
            _state.FuelTotal = targetFuel;
        }
    }

    private void UpdateRefuelGate(DateTimeOffset now)
    {
        var belowStopSpeed = _state.GroundSpeedKts < _config.RefuelStopSpeedKts;
        if (_state.OnGround && belowStopSpeed)
        {
            _stopCandidateSinceUtc ??= now;
        }
        else
        {
            _stopCandidateSinceUtc = null;
        }

        var stopHoldElapsedSec = _stopCandidateSinceUtc.HasValue
            ? (now - _stopCandidateSinceUtc.Value).TotalSeconds
            : 0;

        var holdRequirementSec = _config.RefuelStopHoldSec;
        var holdPercent = holdRequirementSec == 0
            ? (_state.OnGround && belowStopSpeed ? 100 : 0)
            : (int)Math.Clamp((stopHoldElapsedSec / holdRequirementSec) * 100, 0, 100);

        _state.StopHoldProgress = holdPercent;
        _state.RefuelAllowed = _state.OnGround && belowStopSpeed && holdPercent >= 100;
    }

    private string ResolveState(string previousState)
    {
        if (_starvingLatched || _state.TimeToCutSec <= 0 || _state.FuelTotal <= (_state.MinFuelTotal + _config.StarveEpsilon))
        {
            _starvingLatched = true;
            return "STARVING";
        }

        if (_state.TimeToCutSec <= _config.WarningSec)
        {
            return "WARNING";
        }

        return "SAFE";
    }

    private void StartNewCycle(double startFuelTotal, string reason)
    {
        _hasCycle = true;
        _state.StartFuelTotal = SanitizeFuel(startFuelTotal);

        var percentFloorFuel = _state.StartFuelTotal * (_config.MinFuelPercentOfStart / 100.0);
        _state.MinFuelTotal = SanitizeFuel(Math.Max(percentFloorFuel, _config.MinFuelAbsolute));

        var rawDrain = (_state.StartFuelTotal - _state.MinFuelTotal) / _config.IntervalSec;
        _state.DrainPerSecond = SanitizeFuel(Math.Max(rawDrain, 0));

        if (_debugEnabled)
        {
            Console.WriteLine($"[DEBUG] Cycle start ({reason}): startFuelTotal={_state.StartFuelTotal:F3}, minFuelTotal={_state.MinFuelTotal:F3}");
            Console.WriteLine($"[DEBUG] drainPerSecond={_state.DrainPerSecond:F6}");
        }
    }

    private static string ResolveVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static double SanitizeGroundSpeed(double groundSpeedKts)
    {
        if (double.IsNaN(groundSpeedKts) || double.IsInfinity(groundSpeedKts))
        {
            return 0;
        }

        return Math.Max(0, groundSpeedKts);
    }

    private static double SanitizeFuel(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Max(0, value);
    }

    private static double SanitizePercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }
}
