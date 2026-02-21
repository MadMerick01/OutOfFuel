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
    private readonly PeriodicTimer _timer = new(TimeSpan.FromSeconds(1));

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private DateTimeOffset? _lastRefuelUtc;
    private DateTimeOffset? _stopCandidateSinceUtc;

    public StateService(bool debugEnabled, AgentConfig config, ISimDataSource simDataSource)
    {
        _debugEnabled = debugEnabled;
        _config = config;
        _simDataSource = simDataSource;
        _state = new AppState
        {
            Version = ResolveVersion(),
            IntervalSec = _config.IntervalSec,
            WarningSec = _config.WarningSec,
            RefuelPercent = _config.RefuelPercent,
            FuelRampDownSec = _config.FuelRampDownSec,
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
                FuelRampDownSec = _state.FuelRampDownSec,
                OnGround = _state.OnGround,
                GroundSpeedKts = _state.GroundSpeedKts,
                FuelPercent = _state.FuelPercent,
                RefuelAllowed = _state.RefuelAllowed,
                StopHoldProgress = _state.StopHoldProgress,
                LastRefuelSecAgo = _state.LastRefuelSecAgo,
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

    public bool RequestRefuel()
    {
        lock (_sync)
        {
            if (!_state.RefuelAllowed)
            {
                return false;
            }

            _simDataSource.SetFuelPercent(_config.RefuelPercent);
            _state.FuelPercent = _config.RefuelPercent;
            _state.TimeToCutSec = _config.IntervalSec;
            _state.State = "SAFE";
            _lastRefuelUtc = DateTimeOffset.UtcNow;
            _state.LastRefuelSecAgo = 0;
            return true;
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

        lock (_sync)
        {
            _state.Connected = simData.Connected;
            _state.OnGround = simData.OnGround;
            _state.GroundSpeedKts = SanitizeGroundSpeed(simData.GroundSpeedKts);
            _state.FuelPercent = simData.FuelPercent;

            var belowStopSpeed = _state.GroundSpeedKts < _config.RefuelStopSpeedKts;
            if (_state.OnGround && belowStopSpeed)
            {
                _stopCandidateSinceUtc ??= DateTimeOffset.UtcNow;
            }
            else
            {
                _stopCandidateSinceUtc = null;
            }

            var stopHoldElapsedSec = _stopCandidateSinceUtc.HasValue
                ? (DateTimeOffset.UtcNow - _stopCandidateSinceUtc.Value).TotalSeconds
                : 0;
            var holdRequirementSec = _config.RefuelStopHoldSec;
            var holdPercent = holdRequirementSec == 0
                ? (_state.OnGround && belowStopSpeed ? 100 : 0)
                : (int)Math.Clamp((stopHoldElapsedSec / holdRequirementSec) * 100, 0, 100);

            _state.StopHoldProgress = holdPercent;
            _state.RefuelAllowed = _state.OnGround && belowStopSpeed && holdPercent >= 100;

            if (!_state.OnGround && _state.TimeToCutSec > 0)
            {
                _state.TimeToCutSec -= 1;
            }

            if (_lastRefuelUtc.HasValue)
            {
                _state.LastRefuelSecAgo = (int)(DateTimeOffset.UtcNow - _lastRefuelUtc.Value).TotalSeconds;
            }

            var previousState = _state.State;
            _state.State = ResolveState(previousState, _state.TimeToCutSec, _state.OnGround, _config.WarningSec);

            if (_debugEnabled && !string.Equals(previousState, _state.State, StringComparison.Ordinal))
            {
                Console.WriteLine($"[DEBUG] State transition: {previousState} -> {_state.State} (timeToCutSec={_state.TimeToCutSec})");
            }

            _simDataSource.ApplyFuelCut(_state.TimeToCutSec, _state.FuelRampDownSec);
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

    private static string ResolveState(string previousState, int timeToCutSec, bool onGround, int warningSec)
    {
        if (string.Equals(previousState, "STARVING", StringComparison.Ordinal))
        {
            return "STARVING";
        }

        if (onGround)
        {
            return "LANDED";
        }

        if (timeToCutSec <= 0)
        {
            return "STARVING";
        }

        if (timeToCutSec <= warningSec)
        {
            return "WARNING";
        }

        return "SAFE";
    }
}
