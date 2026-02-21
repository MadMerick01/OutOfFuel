using OutOfFuel.Agent.src.Http;
using OutOfFuel.Agent.src.Models;
using OutOfFuel.Agent.src.Services;
using OutOfFuel.Agent.src.Sim;

var debugEnabled = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));

var config = AgentConfig.LoadOrCreate(AppContext.BaseDirectory);
ISimDataSource simDataSource = new SimConnectService(AppContext.BaseDirectory, debugEnabled);

var stateService = new StateService(debugEnabled, config, simDataSource);
var httpServer = new HttpServer(stateService, "http://localhost:8080/");

using var cts = new CancellationTokenSource();
var isStopping = false;
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    if (isStopping)
    {
        return;
    }

    isStopping = true;
    Console.WriteLine("Ctrl+C received. Shutting down...");
    cts.Cancel();
};

Console.WriteLine("OutOfFuel.Agent running at http://localhost:8080");
Console.WriteLine($"Loaded config from {Path.Combine(AppContext.BaseDirectory, AgentConfig.FileName)}");
if (debugEnabled)
{
    Console.WriteLine("Debug logging enabled.");
}
Console.WriteLine("Press Ctrl+C to stop.");

try
{
    await httpServer.StartAsync(cts.Token);
    await stateService.StartAsync(cts.Token);
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException)
{
    // graceful shutdown
}
finally
{
    await httpServer.StopAsync();
    await stateService.StopAsync();
    Console.WriteLine("Shutdown complete.");
}
