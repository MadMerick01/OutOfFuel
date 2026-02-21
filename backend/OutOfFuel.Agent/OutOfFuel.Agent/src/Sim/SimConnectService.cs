using System.Reflection;
using System.Runtime.InteropServices;

namespace OutOfFuel.Agent.src.Sim;

public sealed class SimConnectService : ISimDataSource
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(3);

    private readonly string _simConnectDllPath;
    private readonly bool _debugEnabled;
    private readonly Type _simConnectType;
    private readonly Type _simConnectDataTypeEnum;
    private readonly Type _simConnectPeriodEnum;

    private readonly AutoResetEvent _messageEvent = new(false);

    private object? _simConnect;
    private DateTimeOffset _nextConnectAttemptUtc = DateTimeOffset.MinValue;

    private bool _connected;
    private bool _onGround;
    private double _groundSpeedKts;
    private double _fuelTotalGallons;
    private double _fuelCapacityGallons;
    private DateTimeOffset _lastFuelWriteLogUtc = DateTimeOffset.MinValue;

    public SimConnectService(string appBaseDirectory, bool debugEnabled)
    {
        _debugEnabled = debugEnabled;
        _simConnectDllPath = Path.Combine(appBaseDirectory, "lib", "SimConnect", "Microsoft.FlightSimulator.SimConnect.dll");

        if (!File.Exists(_simConnectDllPath))
        {
            throw new InvalidOperationException(
                "SimConnect managed DLL was not found. Place Microsoft.FlightSimulator.SimConnect.dll in '" +
                "backend/OutOfFuel.Agent/OutOfFuel.Agent/lib/SimConnect/" +
                $"' (resolved runtime path: '{_simConnectDllPath}') and restart OutOfFuel.Agent.");
        }

        var simConnectAssembly = Assembly.LoadFrom(_simConnectDllPath);
        _simConnectType = simConnectAssembly.GetType("Microsoft.FlightSimulator.SimConnect.SimConnect")
            ?? throw new InvalidOperationException("Unable to load SimConnect type Microsoft.FlightSimulator.SimConnect.SimConnect.");
        _simConnectDataTypeEnum = simConnectAssembly.GetType("Microsoft.FlightSimulator.SimConnect.SIMCONNECT_DATATYPE")
            ?? throw new InvalidOperationException("Unable to load SIMCONNECT_DATATYPE enum from SimConnect assembly.");
        _simConnectPeriodEnum = simConnectAssembly.GetType("Microsoft.FlightSimulator.SimConnect.SIMCONNECT_PERIOD")
            ?? throw new InvalidOperationException("Unable to load SIMCONNECT_PERIOD enum from SimConnect assembly.");
    }

    public SimDataSnapshot Poll()
    {
        EnsureConnected();

        if (_simConnect is null)
        {
            return new SimDataSnapshot(false, _onGround, _groundSpeedKts, _fuelTotalGallons, CalculateFuelPercent());
        }

        try
        {
            while (_messageEvent.WaitOne(0))
            {
                Invoke(_simConnect, "ReceiveMessage");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SIMCONNECT] Disconnected while reading: {ex.Message}");
            Disconnect();
        }

        return new SimDataSnapshot(_connected, _onGround, _groundSpeedKts, _fuelTotalGallons, CalculateFuelPercent());
    }

    public double GetTotalFuel()
    {
        return _fuelTotalGallons;
    }

    public void SetTotalFuel(double value)
    {
        if (_simConnect is null)
        {
            return;
        }

        var safeValue = SanitizeNonNegative(value);

        Invoke(
            _simConnect,
            "SetDataOnSimObject",
            EnumValue<DefinitionId>(DefinitionId.FuelSet),
            EnumValue<ObjectId>(ObjectId.User),
            0u,
            0u,
            1u,
            (uint)Marshal.SizeOf<FuelSetData>(),
            [new FuelSetData { FuelTotalQuantityGallons = safeValue }]);

        _fuelTotalGallons = safeValue;

        if (_debugEnabled && DateTimeOffset.UtcNow - _lastFuelWriteLogUtc >= TimeSpan.FromSeconds(1))
        {
            _lastFuelWriteLogUtc = DateTimeOffset.UtcNow;
            Console.WriteLine($"[SIMCONNECT] Fuel write (gallons): {safeValue:F3}");
        }
    }

    private void EnsureConnected()
    {
        if (_simConnect is not null)
        {
            return;
        }

        if (DateTimeOffset.UtcNow < _nextConnectAttemptUtc)
        {
            return;
        }

        _nextConnectAttemptUtc = DateTimeOffset.UtcNow.Add(ReconnectInterval);

        try
        {
            Connect();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SIMCONNECT] Connection attempt failed: {ex.Message}. Retrying in {ReconnectInterval.TotalSeconds:0} seconds.");
            Disconnect();
        }
    }

    private void Connect()
    {
        Console.WriteLine("[SIMCONNECT] Attempting connection...");

        _simConnect = Activator.CreateInstance(
            _simConnectType,
            "OutOfFuel.Agent",
            IntPtr.Zero,
            0u,
            _messageEvent,
            0u) ?? throw new InvalidOperationException("Failed to create SimConnect instance.");

        HookEvents(_simConnect);
        RegisterDataDefinition(_simConnect);

        var periodSecond = Enum.Parse(_simConnectPeriodEnum, "SECOND", ignoreCase: false);

        Invoke(
            _simConnect,
            "RequestDataOnSimObject",
            EnumValue<DefinitionId>(DefinitionId.Primary),
            EnumValue<RequestId>(RequestId.Primary),
            EnumValue<ObjectId>(ObjectId.User),
            periodSecond,
            0u,
            0u,
            0u,
            0u);

        _connected = true;
        Console.WriteLine("[SIMCONNECT] Connected.");
    }

    private void RegisterDataDefinition(object simConnect)
    {
        var float64 = Enum.Parse(_simConnectDataTypeEnum, "FLOAT64", ignoreCase: false);

        Invoke(simConnect, "AddToDataDefinition", EnumValue<DefinitionId>(DefinitionId.Primary), "SIM ON GROUND", "Bool", float64, 0.0f, uint.MaxValue);
        Invoke(simConnect, "AddToDataDefinition", EnumValue<DefinitionId>(DefinitionId.Primary), "GROUND VELOCITY", "knots", float64, 0.0f, uint.MaxValue);
        // Using FUEL TOTAL QUANTITY in gallons for both reads and writes.
        Invoke(simConnect, "AddToDataDefinition", EnumValue<DefinitionId>(DefinitionId.Primary), "FUEL TOTAL QUANTITY", "gallons", float64, 0.0f, uint.MaxValue);
        Invoke(simConnect, "AddToDataDefinition", EnumValue<DefinitionId>(DefinitionId.Primary), "FUEL TOTAL CAPACITY", "gallons", float64, 0.0f, uint.MaxValue);

        var registerMethod = _simConnectType.GetMethods()
            .FirstOrDefault(m => m.Name == "RegisterDataDefineStruct" && m.IsGenericMethodDefinition)
            ?? throw new InvalidOperationException("Could not find RegisterDataDefineStruct generic method.");

        // Writes also use total fuel quantity in gallons (not percent, and no per-tank writes).
        Invoke(simConnect, "AddToDataDefinition", EnumValue<DefinitionId>(DefinitionId.FuelSet), "FUEL TOTAL QUANTITY", "gallons", float64, 0.0f, uint.MaxValue);

        registerMethod.MakeGenericMethod(typeof(SimData)).Invoke(simConnect, [EnumValue<DefinitionId>(DefinitionId.Primary)]);
        registerMethod.MakeGenericMethod(typeof(FuelSetData)).Invoke(simConnect, [EnumValue<DefinitionId>(DefinitionId.FuelSet)]);
    }

    private void HookEvents(object simConnect)
    {
        AddEventHandler(simConnect, "OnRecvOpen", (Action<object, object>)((_, __) =>
        {
            if (_debugEnabled)
            {
                Console.WriteLine("[SIMCONNECT] Open event received.");
            }
        }));

        AddEventHandler(simConnect, "OnRecvQuit", (Action<object, object>)((_, __) =>
        {
            Console.WriteLine("[SIMCONNECT] Simulator quit event received.");
            Disconnect();
        }));

        AddEventHandler(simConnect, "OnRecvException", (Action<object, object>)((_, evt) =>
        {
            Console.WriteLine($"[SIMCONNECT] Exception event: {evt}");
        }));

        AddEventHandler(simConnect, "OnRecvSimobjectData", (Action<object, object>)((_, evt) =>
        {
            var dataProperty = evt.GetType().GetProperty("dwData")
                ?? throw new InvalidOperationException("SimConnect data event missing dwData property.");

            var dwData = dataProperty.GetValue(evt) as Array;
            if (dwData is null || dwData.Length == 0)
            {
                return;
            }

            var payload = dwData.GetValue(0);
            if (payload is not SimData simData)
            {
                return;
            }

            var nextOnGround = simData.OnGround >= 0.5;
            var nextGroundSpeed = simData.GroundSpeedKts;
            var nextFuelTotal = SanitizeNonNegative(simData.FuelTotalQuantityGallons);
            var nextFuelCapacity = SanitizeNonNegative(simData.FuelTotalCapacityGallons);
            var nextFuelPercent = nextFuelCapacity > 0 ? (nextFuelTotal / nextFuelCapacity) * 100.0 : 0;
            var previousFuelPercent = CalculateFuelPercent();

            if (nextOnGround != _onGround ||
                Math.Abs(nextGroundSpeed - _groundSpeedKts) > 0.25 ||
                Math.Abs(nextFuelPercent - previousFuelPercent) > 0.25)
            {
                Console.WriteLine(
                    $"[SIMCONNECT] Data update: onGround={nextOnGround}, groundSpeedKts={nextGroundSpeed:F1}, fuelTotalGal={nextFuelTotal:F2}");
            }

            _onGround = nextOnGround;
            _groundSpeedKts = nextGroundSpeed;
            _fuelTotalGallons = nextFuelTotal;
            _fuelCapacityGallons = nextFuelCapacity;
        }));
    }

    private void Disconnect()
    {
        if (_simConnect is not null)
        {
            try
            {
                Invoke(_simConnect, "Dispose");
            }
            catch
            {
                // ignore dispose errors
            }
        }

        _simConnect = null;

        if (_connected)
        {
            Console.WriteLine("[SIMCONNECT] Disconnected.");
        }

        _connected = false;
    }

    public void Dispose()
    {
        Disconnect();
        _messageEvent.Dispose();
    }

    private double CalculateFuelPercent()
    {
        if (_fuelCapacityGallons <= 0)
        {
            return 0;
        }

        return Math.Clamp((_fuelTotalGallons / _fuelCapacityGallons) * 100.0, 0, 100);
    }

    private static double SanitizeNonNegative(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Max(0, value);
    }

    private static object EnumValue<TEnum>(TEnum value)
        where TEnum : Enum
    {
        return value;
    }

    private static void Invoke(object instance, string methodName, params object[] args)
    {
        var methods = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => m.Name == methodName && m.GetParameters().Length == args.Length)
            .ToArray();

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var converted = new object[args.Length];
            var compatible = true;

            for (var i = 0; i < args.Length; i++)
            {
                if (!TryConvertArg(args[i], parameters[i].ParameterType, out var convertedArg))
                {
                    compatible = false;
                    break;
                }

                converted[i] = convertedArg!;
            }

            if (!compatible)
            {
                continue;
            }

            method.Invoke(instance, converted);
            return;
        }

        throw new InvalidOperationException($"Method '{methodName}' with compatible signature was not found on {instance.GetType().Name}.");
    }

    private static bool TryConvertArg(object? arg, Type targetType, out object? converted)
    {
        if (arg is null)
        {
            converted = null;
            return !targetType.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        var argType = arg.GetType();
        if (targetType.IsAssignableFrom(argType))
        {
            converted = arg;
            return true;
        }

        try
        {
            if (targetType.IsEnum)
            {
                var raw = Convert.ChangeType(arg, Enum.GetUnderlyingType(targetType));
                converted = Enum.ToObject(targetType, raw!);
                return true;
            }

            converted = Convert.ChangeType(arg, targetType);
            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }

    private static void AddEventHandler(object instance, string eventName, Delegate handler)
    {
        var eventInfo = instance.GetType().GetEvent(eventName)
            ?? throw new InvalidOperationException($"Event '{eventName}' was not found on SimConnect type.");

        var convertedDelegate = Delegate.CreateDelegate(eventInfo.EventHandlerType!, handler.Target, handler.Method);
        eventInfo.AddEventHandler(instance, convertedDelegate);
    }

    private enum DefinitionId
    {
        Primary = 0,
        FuelSet = 1,
    }

    private enum RequestId
    {
        Primary = 0,
    }

    private enum ObjectId : uint
    {
        User = 0,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct SimData
    {
        public double OnGround;
        public double GroundSpeedKts;
        public double FuelTotalQuantityGallons;
        public double FuelTotalCapacityGallons;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi, Pack = 1)]
    private struct FuelSetData
    {
        public double FuelTotalQuantityGallons;
    }
}
