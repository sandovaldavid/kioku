using Kioku.Mcp.Server.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Exercises the HTTP-SSE transport pipeline (CORS, ApiKeyMiddleware, /health, /mcp) on an
/// ephemeral port. Mirrors the wiring in Program.cs's RunHttpAsync rather than invoking it
/// directly, since it's a local function inside top-level statements and not reachable here.
/// </summary>
public class HttpTransportTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string _baseUrl = string.Empty;

    private async Task<string> StartServerAsync(string? apiKey = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var config = new KiokuConfiguration { VaultPath = "/tmp", ApiKey = apiKey };
        builder.Services.AddSingleton(config);
        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy
                    .WithOrigins("http://localhost", "app://obsidian.md")
                    .AllowAnyHeader()
                    .AllowAnyMethod()));
        builder.Services.AddMcpServer().WithHttpTransport();

        _app = builder.Build();
        _app.UseCors();
        _app.UseMiddleware<ApiKeyMiddleware>();
        _app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        _app.MapMcp("/mcp");

        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses;
        return addresses.First();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync($"{_baseUrl}/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsExemptFromApiKey()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = new HttpClient();

        var response = await http.GetAsync($"{_baseUrl}/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_WithoutApiKeyConfigured_RespondsToInitialize()
    {
        _baseUrl = await StartServerAsync();

        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri($"{_baseUrl}/mcp"),
        });

        await using var client = await McpClient.CreateAsync(transport);

        Assert.NotNull(client.ServerInfo);
    }

    [Fact]
    public async Task Mcp_WithApiKeyConfigured_WithoutBearerToken_Returns401()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = new HttpClient();

        var response = await http.PostAsync(
            $"{_baseUrl}/mcp",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Cors_AllowedOrigin_ReceivesAllowOriginHeader()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/health");
        request.Headers.Add("Origin", "http://localhost");

        var response = await http.SendAsync(request);

        Assert.True(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Cors_DisallowedOrigin_OmitsAllowOriginHeader()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/health");
        request.Headers.Add("Origin", "http://evil.example.com");

        var response = await http.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
