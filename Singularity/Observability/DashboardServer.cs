using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Singularity.Observability;

/// <summary>
///     Tiny read-only status page (HttpListener, no extra dependencies). Serves aggregate stats only. Binds to
///     loopback by default; any other address requires <c>Dashboard:Token</c>.
///     Config: Dashboard:Enabled (true), Dashboard:Host (127.0.0.1), Dashboard:Port (8787), Dashboard:Token.
/// </summary>
internal sealed class DashboardServer(
    StatsService stats,
    BotMetrics metrics,
    IConfiguration configuration,
    ILogger<DashboardServer> logger) : BackgroundService
{
    private static readonly string Html = LoadHtml();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Dashboard:Enabled", true))
        {
            logger.LogDebug("Dashboard disabled.");
            return;
        }

        var host = configuration["Dashboard:Host"] ?? "127.0.0.1";
        var port = configuration.GetValue("Dashboard:Port", 8787);
        var token = configuration["Dashboard:Token"];
        var loopback = host is "127.0.0.1" or "localhost" or "::1" or "[::1]";

        if (!loopback && string.IsNullOrEmpty(token))
        {
            logger.LogError("Refusing to serve the dashboard on {Host} without Dashboard:Token set.", host);
            return;
        }

        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"127.0.0.1:{port}", $"localhost:{port}", $"[::1]:{port}", $"{host}:{port}"
        };

        var prefix = $"http://{(host.Contains(':') && !host.StartsWith('[') ? $"[{host}]" : host)}:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);

        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Dashboard could not start on {Prefix}; continuing without it.", prefix);
            return;
        }

        logger.LogInformation("Dashboard: {Prefix}{Auth}", prefix, string.IsNullOrEmpty(token) ? "" : "?key=<token>");
        await using var stopRegistration = stoppingToken.Register(listener.Stop);

        while (!stoppingToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                metrics.Error("dashboard.accept");
                logger.LogWarning(ex, "The dashboard could not accept a connection.");
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ContinueWith(_ => { }, CancellationToken.None); // no hot loop
                continue;
            }

            _ = Task.Run(() => HandleAsync(context, token, allowedHosts), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, string? token, HashSet<string> allowedHosts)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["Content-Security-Policy"] =
                "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self'";

            // Defends against DNS-rebinding: only answer for the names/addresses we were configured to serve.
            if (!allowedHosts.Contains(request.UserHostName ?? ""))
            {
                await WriteAsync(response, 403, "text/plain", "Forbidden");
                return;
            }

            if (request.HttpMethod != "GET")
            {
                response.Headers["Allow"] = "GET";
                await WriteAsync(response, 405, "text/plain", "Method not allowed");
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";

            if (path == "/healthz")
            {
                await WriteAsync(response, 200, "text/plain", "ok");
                return;
            }

            if (!IsAuthorized(request, token))
            {
                response.Headers["WWW-Authenticate"] = "Bearer";
                await WriteAsync(response, 401, "text/plain", "Unauthorized. Open the page with ?key=<Dashboard:Token>.");
                return;
            }

            switch (path)
            {
                case "/":
                case "/index.html":
                    await WriteAsync(response, 200, "text/html; charset=utf-8", Html);
                    break;
                case "/api/stats":
                    await WriteAsync(response, 200, "application/json",
                        JsonSerializer.Serialize(stats.Snapshot(), DashboardJsonContext.Default.StatsSnapshot));
                    break;
                default:
                    await WriteAsync(response, 404, "text/plain", "Not found");
                    break;
            }
        }
        catch (Exception ex)
        {
            // A bug in serving or building the stats: tell the browser, and make sure the operator knows.
            metrics.Error("dashboard.request");
            logger.LogWarning(ex, "The dashboard failed to answer {Method} {Path}.", request.HttpMethod, request.Url?.AbsolutePath);
            try
            {
                await WriteAsync(response, 500, "text/plain", "Internal error. See the bot's log.");
            }
            catch
            {
                try { response.Abort(); } catch { /* connection already gone */ }
            }
        }
    }

    private static bool IsAuthorized(HttpListenerRequest request, string? token)
    {
        if (string.IsNullOrEmpty(token)) return true;

        var supplied = request.Headers["Authorization"] is { } header && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header[7..]
            : request.QueryString["key"];

        return supplied is not null &&
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(token));
    }

    private static async Task WriteAsync(HttpListenerResponse response, int status, string contentType, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        response.StatusCode = status;
        response.ContentType = contentType;
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static string LoadHtml()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dashboard.html")
                           ?? throw new InvalidOperationException("dashboard.html is not embedded.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
