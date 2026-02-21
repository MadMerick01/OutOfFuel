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
                LastRefuelSecAgo = _state.LastRefuelSecAgo,
            };
        }
    }

    public void RequestRefuel()
    {
        lock (_sync)
        {
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

            _state.TimeToCutSec = Math.Max(0, _state.TimeToCutSec - 1);

            if (_lastRefuelUtc.HasValue)
            {
                _state.LastRefuelSecAgo = (int)(DateTimeOffset.UtcNow - _lastRefuelUtc.Value).TotalSeconds;
            }

            var previousState = _state.State;
            _state.State = ResolveState(_state.TimeToCutSec, _state.OnGround, _config.WarningSec);

            if (_debugEnabled && !string.Equals(previousState, _state.State, StringComparison.Ordinal))
            {
                Console.WriteLine($"[DEBUG] State transition: {previousState} -> {_state.State} (timeToCutSec={_state.TimeToCutSec})");
            }

            if (_debugEnabled && _refuelRequested)
            {
                Console.WriteLine("[DEBUG] Refuel requested.");
                _refuelRequested = false;
            }
        }
    }

    private static string ResolveState(int timeToCutSec, bool onGround, int warningSec)
    {
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
