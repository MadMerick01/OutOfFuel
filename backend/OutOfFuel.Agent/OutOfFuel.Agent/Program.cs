using OutOfFuel.Agent.src.Config;
using OutOfFuel.Agent.src.Http;
using OutOfFuel.Agent.src.Sim;

var config = new AgentConfiguration();
var simBridge = new SimBridge();
var overlayServer = new OverlayHttpServer();

Console.WriteLine("OutOfFuel.Agent bootstrap complete.");
Console.WriteLine($"Overlay root: {config.OverlayRoot}");
Console.WriteLine("SimConnect integration is intentionally not implemented yet.");

_ = simBridge;
_ = overlayServer;
