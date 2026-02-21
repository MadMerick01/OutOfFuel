using System.Net;
using System.Text;
using System.Text.Json;
using OutOfFuel.Agent.src.Services;

namespace OutOfFuel.Agent.src.Http;

public sealed class HttpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly StateService _stateService;
    private readonly HttpListener _listener = new();

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public HttpServer(StateService stateService, string prefix)
    {
        _stateService = stateService;
        _listener.Prefixes.Add(prefix);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loopTask = Task.Run(() => AcceptLoopAsync(_loopCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_loopCts is null)
        {
            return;
        }

        _loopCts.Cancel();
        _listener.Stop();

        if (_loopTask is not null)
        {
            await _loopTask;
        }

        _loopCts.Dispose();
        _listener.Close();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext? context = null;

            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (context is null)
            {
                continue;
            }

            _ = Task.Run(() => HandleRequestAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        AddCorsHeaders(context.Response);

        if (context.Request.HttpMethod == "OPTIONS")
        {
            context.Response.StatusCode = (int)HttpStatusCode.NoContent;
            context.Response.Close();
            return;
        }

        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        if (context.Request.HttpMethod == "GET" && path == "/state")
        {
            var state = _stateService.GetStateSnapshot();
            await WriteJsonAsync(context.Response, state, HttpStatusCode.OK);
            return;
        }

        if (context.Request.HttpMethod == "POST" && path == "/refuel")
        {
            var ok = _stateService.RequestRefuel();
            if (!ok)
            {
                await WriteJsonAsync(context.Response, new { ok = false, reason = "not allowed" }, HttpStatusCode.OK);
                return;
            }

            await WriteJsonAsync(context.Response, new { ok = true }, HttpStatusCode.OK);
            return;
        }

        await WriteJsonAsync(context.Response, new { error = "Not found" }, HttpStatusCode.NotFound);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object payload, HttpStatusCode statusCode)
    {
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";

        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.OutputStream.Close();
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }
}
