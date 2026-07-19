using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Http;
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
/// Exercises the production Streamable HTTP security and health pipeline on an ephemeral port.
/// </summary>
public class HttpTransportTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string _baseUrl = string.Empty;

    private async Task<string> StartServerAsync(
        string? apiKey = null,
        bool ready = false,
        IReadOnlyList<string>? allowedOrigins = null,
        IReadOnlyList<string>? trustedProxies = null,
        long maxRequestBodyBytes = 1024 * 1024,
        int requestTimeoutSeconds = 300)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var config = new KiokuConfiguration
        {
            VaultPath = "/tmp/should-never-appear-in-health",
            ApiKey = apiKey,
            HttpAllowedOrigins = allowedOrigins ??
                ["http://localhost", "app://obsidian.md"],
            HttpTrustedProxies = trustedProxies ?? [],
            HttpMaxRequestBodyBytes = maxRequestBodyBytes,
            HttpRequestTimeoutSeconds = requestTimeoutSeconds,
        };
        builder.Services.AddSingleton(config);
        HttpTransportSecurity.ConfigureBuilder(builder, config);
        builder.Services.AddMcpServer().WithHttpTransport();

        _app = builder.Build();
        HttpTransportSecurity.Use(_app, config);
        HttpTransportSecurity.MapHealthEndpoints(_app);
        _app.MapGet("/client-ip", (HttpContext context) =>
            Results.Text(context.Connection.RemoteIpAddress?.ToString() ?? "unknown"));
        _app.MapPost("/mcp/slow", async (HttpContext context) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), context.RequestAborted);
            return Results.Ok();
        });
        _app.MapMcp("/mcp");

        if (ready)
        {
            var readiness = _app.Services.GetRequiredService<HttpReadinessState>();
            readiness.MarkIndexReady();
            readiness.SetOptionalDependencies(
                embeddingsAvailable: false,
                generationConfigured: false,
                generationAvailable: false);
        }

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
    public async Task Liveness_IsPublicAndMinimal()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = new HttpClient();

        var response = await http.GetAsync($"{_baseUrl}/health/live");
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal("ok", json.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_RequiresAuthenticationWhenApiKeyIsConfigured()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret", ready: true);
        using var http = new HttpClient();

        var response = await http.GetAsync($"{_baseUrl}/health/ready");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_Returns503UntilIndexIsReadyWithoutExposingHostData()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = CreateAuthenticatedClient("secret");

        var response = await http.GetAsync($"{_baseUrl}/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"index\":\"starting\"", body);
        Assert.DoesNotContain("/tmp", body);
        Assert.DoesNotContain("secret", body);
    }

    [Fact]
    public async Task Readiness_Returns200WhenIndexIsReady()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret", ready: true);
        using var http = CreateAuthenticatedClient("secret");

        var response = await http.GetAsync($"{_baseUrl}/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ready\"", body);
        Assert.Contains("\"generation\":\"disabled\"", body);
    }

    [Fact]
    public async Task Readiness_Returns503WhenIndexInitializationFailed()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        _app!.Services.GetRequiredService<HttpReadinessState>().MarkIndexFailed();
        using var http = CreateAuthenticatedClient("secret");

        var response = await http.GetAsync($"{_baseUrl}/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("\"index\":\"failed\"", body);
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

        var response = await PostInitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_WithInvalidBearerToken_Returns401()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = CreateAuthenticatedClient("incorrect-secret");

        var response = await PostInitializeAsync(http);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_WithValidBearerToken_ReachesTransport()
    {
        _baseUrl = await StartServerAsync(apiKey: "secret");
        using var http = CreateAuthenticatedClient("secret");

        var response = await PostInitializeAsync(http);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllowedOrigin_IsAcceptedAndCorsAddsAllowOriginHeader()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/health/live");
        request.Headers.Add("Origin", "http://localhost");

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "http://localhost",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Theory]
    [InlineData("http://evil.example.com")]
    [InlineData("https://allowed.example@evil.example")]
    [InlineData("http://localhost/path")]
    [InlineData("null")]
    public async Task DisallowedOrMalformedOrigin_Returns403(string origin)
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/health/live");
        request.Headers.TryAddWithoutValidation("Origin", origin);

        var response = await http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task MissingOrigin_IsAllowedForNonBrowserClients()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();

        var response = await http.GetAsync($"{_baseUrl}/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task TrustedProxy_UpdatesRemoteAddressFromForwardedHeader()
    {
        _baseUrl = await StartServerAsync(trustedProxies: ["127.0.0.1"]);
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/client-ip");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        var response = await http.SendAsync(request);

        Assert.Equal("203.0.113.10", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UntrustedProxy_CannotOverrideRemoteAddress()
    {
        _baseUrl = await StartServerAsync();
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/client-ip");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "https");

        var response = await http.SendAsync(request);

        Assert.Equal("127.0.0.1", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task OversizedMcpRequest_Returns413()
    {
        _baseUrl = await StartServerAsync(maxRequestBodyBytes: 1024);
        using var http = new HttpClient();
        using var content = new ByteArrayContent(new byte[2048]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        var response = await http.PostAsync($"{_baseUrl}/mcp", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task SlowMcpPost_Returns503WithoutTimingOutEventStreams()
    {
        _baseUrl = await StartServerAsync(requestTimeoutSeconds: 1);
        using var http = new HttpClient();

        var response = await http.PostAsync(
            $"{_baseUrl}/mcp/slow",
            new StringContent("{}", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("timed out", await response.Content.ReadAsStringAsync());
    }

    private static HttpClient CreateAuthenticatedClient(string token)
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<HttpResponseMessage> PostInitializeAsync(HttpClient http)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/mcp");
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Content = new StringContent(
            """
            {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}
            """,
            Encoding.UTF8,
            "application/json");
        return await http.SendAsync(request);
    }
}
