using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class UtilityToolsTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public UtilityToolsTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task WaitForBacklogToClearAsync(EmbeddingService service, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (service.EmbeddingBacklog > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public void GetServerStatus_NoEmbeddingService_OmitsEmbeddingProgressFields()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var tools = new UtilityTools(_fixture.Index, config);

        var result = tools.get_server_status();

        Assert.StartsWith("[online] Kioku MCP Server", result);
        Assert.Contains("Health: healthy", result);
        Assert.Contains("Index ready:", result);
        Assert.DoesNotContain("Embedding backlog", result);
    }

    [Fact]
    public async Task GetServerStatus_OllamaUnavailable_OmitsEmbeddingProgressFields()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, EmbeddingModel = "nomic-embed-text" };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)))));
        await embedding.InitializeAsync([]);

        var tools = new UtilityTools(_fixture.Index, config, embedding);

        var result = tools.get_server_status();

        Assert.DoesNotContain("Embedding backlog", result);
        Assert.Contains("[info] Unavailable", result);
    }

    [Fact]
    public async Task GetServerStatus_OllamaAvailableWithNoBacklog_ShowsZeroBacklog()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, EmbeddingModel = "nomic-embed-text" };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((req, _) => req.Method == HttpMethod.Get
                ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) }))));
        await embedding.InitializeAsync([]);

        var tools = new UtilityTools(_fixture.Index, config, embedding);

        var result = tools.get_server_status();

        Assert.Contains("Embedding backlog: 0", result);
        Assert.Contains("Embedding rate:", result);
        Assert.Contains("Estimated remaining: 0s (backlog clear)", result);
    }

    [Fact]
    public async Task GetServerStatus_BacklogInProgress_ShowsNonZeroBacklog()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, EmbeddingModel = "nomic-embed-text" };
        var embedding = new EmbeddingService(config, NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler(async (req, ct) =>
            {
                if (req.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }

                await Task.Delay(200, ct);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) };
            })));

        // A brand-new EmbeddingService has an empty cache, so every note in the fixture is
        // "new" and gets queued for background embedding.
        await embedding.InitializeAsync(_fixture.Index.GetAllNotes());

        var tools = new UtilityTools(_fixture.Index, config, embedding);
        var result = tools.get_server_status();

        Assert.Matches(@"Embedding backlog: [1-9]\d*", result);

        await WaitForBacklogToClearAsync(embedding);
    }

    [Fact]
    public void GetServerCapabilities_DefaultProfileIsStableAndGated()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var tools = new UtilityTools(_fixture.Index, config);

        using var document = JsonDocument.Parse(tools.get_server_capabilities());
        Assert.Equal(
            KiokuCapabilityCatalog.CoordinationProfileId,
            document.RootElement.GetProperty("profile_id").GetString());
        Assert.Equal(
            KiokuCapabilityCatalog.CoordinationProfileVersion,
            document.RootElement.GetProperty("profile_version").GetInt32());
        Assert.Equal("2.3.0", document.RootElement.GetProperty("server_version").GetString());
        Assert.False(document.RootElement.GetProperty("capability_group").GetProperty("enabled").GetBoolean());
        Assert.Equal("gated", document.RootElement.GetProperty("rollout").GetProperty("status").GetString());
        Assert.False(document.RootElement.GetProperty("capabilities").GetProperty("coordination.cas").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void GetServerCapabilities_ReportsExplicitlyEnabledCoordinationFeatures()
    {
        Directory.CreateDirectory(Path.Combine(_fixture.VaultPath, ".kioku"));
        File.WriteAllText(
            Path.Combine(_fixture.VaultPath, ".kioku", "config.yml"),
            "capabilities:\n  require_explicit: true\n  enabled:\n    - coordination\n");
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var tools = new UtilityTools(_fixture.Index, config, vaultConfig: vaultConfig);

        using var document = JsonDocument.Parse(tools.get_server_capabilities());
        Assert.True(document.RootElement.GetProperty("capability_group").GetProperty("enabled").GetBoolean());
        Assert.True(document.RootElement.GetProperty("capabilities").GetProperty("coordination.claims").GetProperty("enabled").GetBoolean());
        Assert.Equal("gated", document.RootElement.GetProperty("rollout").GetProperty("status").GetString());
    }
}
