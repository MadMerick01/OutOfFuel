using OutOfFuel.Agent.src.Http;
using OutOfFuel.Agent.src.Services;

var debugEnabled = args.Any(a => string.Equals(a, "--debug", StringComparison.OrdinalIgnoreCase));

var stateService = new StateService(debugEnabled);
var httpServer = new HttpServer(stateService, "http://localhost:8080/");

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

Console.WriteLine("OutOfFuel.Agent running at http://localhost:8080");
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
