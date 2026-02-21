using OutOfFuel.Agent.src.Http;
using OutOfFuel.Agent.src.Models;
using OutOfFuel.Agent.src.Services;

var debugEnabled = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));

var config = AgentConfig.LoadOrCreate(AppContext.BaseDirectory);
var stateService = new StateService(debugEnabled, config);
var httpServer = new HttpServer(stateService, "http://localhost:8080/");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("OutOfFuel.Agent running at http://localhost:8080");
Console.WriteLine($"Loaded config from {Path.Combine(AppContext.BaseDirectory, AgentConfig.FileName)}");
if (debugEnabled)
{
    Console.WriteLine("Debug logging enabled.");
}
Console.WriteLine("Press Ctrl+C to stop.");

await stateService.StartAsync(cts.Token);
await httpServer.StartAsync(cts.Token);

await Task.Delay(Timeout.Infinite, cts.Token).ContinueWith(_ => { }, TaskScheduler.Default);

await httpServer.StopAsync();
await stateService.StopAsync();
