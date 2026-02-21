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
    private bool _refuelRequested;
    private DateTimeOffset? _stopCandidateSinceUtc;

    public StateService(bool debugEnabled, AgentConfig config, ISimDataSource simDataSource)
    {
        _debugEnabled = debugEnabled;
        _config = config;
        _simDataSource = simDataSource;
        _state = new AppState
        {
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

    public void RequestRefuel()
    {
        lock (_sync)
        {
            if (!_state.RefuelAllowed)
            {
                return;
            }

            _refuelRequested = true;
            _lastRefuelUtc = DateTimeOffset.UtcNow;
            _state.LastRefuelSecAgo = 0;
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
            _state.GroundSpeedKts = simData.GroundSpeedKts;
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

            var refuelOccurred = _refuelRequested;
            if (refuelOccurred)
            {
                _state.TimeToCutSec = _config.IntervalSec;
                _refuelRequested = false;
            }

            if (!_state.OnGround && _state.TimeToCutSec > 0)
            {
                _state.TimeToCutSec -= 1;
            }

            if (_lastRefuelUtc.HasValue)
            {
                _state.LastRefuelSecAgo = (int)(DateTimeOffset.UtcNow - _lastRefuelUtc.Value).TotalSeconds;
            }

            var previousState = _state.State;
            _state.State = ResolveState(previousState, _state.TimeToCutSec, _state.OnGround, _config.WarningSec, refuelOccurred);

            if (_debugEnabled && !string.Equals(previousState, _state.State, StringComparison.Ordinal))
            {
                Console.WriteLine($"[DEBUG] State transition: {previousState} -> {_state.State} (timeToCutSec={_state.TimeToCutSec})");
            }

            if (_debugEnabled && refuelOccurred)
            {
                Console.WriteLine("[DEBUG] Refuel requested.");
            }

            _simDataSource.ApplyFuelCut(_state.TimeToCutSec, _state.FuelRampDownSec);
        }
    }

    private static string ResolveState(string previousState, int timeToCutSec, bool onGround, int warningSec, bool refuelOccurred)
    {
        if (string.Equals(previousState, "STARVING", StringComparison.Ordinal) && !refuelOccurred)
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
