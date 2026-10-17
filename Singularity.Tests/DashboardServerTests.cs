using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Singularity.Features.Music.Models;
using Singularity.Observability;

namespace Singularity.Tests;

public sealed class DashboardServerTests : IAsyncLifetime
{
    private readonly int _port;
    private readonly BotMetrics _metrics = new();
    private readonly LogBuffer _buffer = new();
    private IHost _host = null!;
    private HttpClient _http = null!;

    public DashboardServerTests()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        _port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
    }

    public async Task InitializeAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Dashboard:Port"] = _port.ToString() });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new BufferLoggerProvider(_buffer, _metrics));
        builder.Services.AddSingleton(_metrics);
        builder.Services.AddSingleton(_buffer);
        builder.Services.AddSingleton(new Singularity.Features.Music.MusicOptions());
        builder.Services.AddSingleton<GuildAudioInstanceOrchestrator>();
        builder.Services.AddSingleton<StatsService>();
        builder.Services.AddHostedService<DashboardServer>();

        _host = builder.Build();
        await _host.StartAsync();

        _http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{_port}") };
        for (var i = 0; i < 50; i++) // wait until the listener is up
        {
            try { if ((await _http.GetAsync("/healthz")).IsSuccessStatusCode) return; } catch (HttpRequestException) { }
            await Task.Delay(100);
        }
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _host.StopAsync();
        _host.Dispose();
    }

    private async Task<JsonElement> StatsAsync()
    {
        using var doc = JsonDocument.Parse(await _http.GetStringAsync("/api/stats"));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Serves_the_embedded_page_and_health_check()
    {
        var page = await _http.GetAsync("/");
        Assert.True(page.IsSuccessStatusCode);
        Assert.Contains("Recent events", await page.Content.ReadAsStringAsync());
        Assert.True(page.Headers.Contains("Content-Security-Policy"));
        Assert.Equal("ok", await _http.GetStringAsync("/healthz"));
    }

    [Fact]
    public async Task Stats_reflect_the_metrics()
    {
        _metrics.Command("play");
        _metrics.Command("play");
        _metrics.Command("skip");
        _metrics.TrackStarted();
        _metrics.ResolveTime(420);

        var stats = await StatsAsync();

        Assert.Equal(3, stats.GetProperty("commands").GetProperty("total").GetInt64());
        Assert.Equal(2, stats.GetProperty("commands").GetProperty("byName").GetProperty("play").GetInt64());
        Assert.Equal(1, stats.GetProperty("playback").GetProperty("tracksStarted").GetInt64());
        Assert.Equal(420, stats.GetProperty("resolver").GetProperty("avgResolveMs").GetDouble());
        Assert.True(stats.GetProperty("process").GetProperty("workingSetMb").GetDouble() > 0);
    }

    [Fact]
    public async Task Stats_never_contain_guild_user_or_channel_identifiers()
    {
        var logger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Singularity.Test");
        using (logger.BeginScope("guild={GuildId} user={UserId}", 123456789012345678UL, 987654321098765432UL))
        {
            logger.LogInformation("Now playing in {GuildId}", 123456789012345678UL);
            logger.LogWarning(new InvalidOperationException("boom 111111111111111111"), "Failed for user 987654321098765432");
        }

        var json = await _http.GetStringAsync("/api/stats");

        Assert.DoesNotMatch(new Regex(@"\d{17,20}"), json);
        Assert.DoesNotContain("guild=", json);
        Assert.DoesNotContain("user=", json);
        Assert.Contains("[id]", json);
        Assert.Equal(1, (await StatsAsync()).GetProperty("logs").GetProperty("warnings").GetInt64());
    }

    [Fact]
    public async Task Unknown_paths_and_methods_are_refused()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync("/nope")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await _http.PostAsync("/api/stats", new StringContent(""))).StatusCode);
    }

    [Fact]
    public async Task A_foreign_host_header_is_refused()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/stats");
        request.Headers.Host = "evil.example.com";

        Assert.False((await _http.SendAsync(request)).IsSuccessStatusCode);
    }
}
