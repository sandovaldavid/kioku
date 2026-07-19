using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class PluginIntegrationToolsTests : IClassFixture<VaultFixture>
{
    private readonly VaultFixture _fixture;

    public PluginIntegrationToolsTests(VaultFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("NOTE")]
    public async Task Lint_InvalidScope_ReturnsValidationError(string scope)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = 1 };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var tools = new PluginIntegrationTools(_fixture.Index, bridge);

        var result = await tools.lint(scope);

        Assert.Equal($"[error] Invalid lint scope '{scope}'. Valid scopes: note, vault.", result);
    }

    [Fact]
    public async Task Lint_Note_SendsNotePathToLinter()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var tools = new PluginIntegrationTools(_fixture.Index, bridge);

        var serverSide = ReceiveAndSucceedAsync(server, "run-linter");
        var result = await tools.lint("note", "Note One");
        var request = await serverSide;

        Assert.Equal("[ok] Linter executed on 'Note One.md'.", result);
        Assert.Equal("Note One.md", request.GetProperty("payload").GetProperty("notePath").GetString());
    }

    [Fact]
    public async Task Lint_Vault_SendsVaultLinterCommandWithoutReadinessRequirement()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        using var unreadyIndex = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = server.Port };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var tools = new PluginIntegrationTools(unreadyIndex, bridge);

        var serverSide = ReceiveAndSucceedAsync(server, "run-linter-vault");
        var result = await tools.lint("vault");
        var request = await serverSide;

        Assert.Equal("[ok] Vault-wide linter started. Check Obsidian for progress.", result);
        Assert.Equal("run-linter-vault", request.GetProperty("command").GetString());
        Assert.Empty(request.GetProperty("payload").EnumerateObject());
    }

    [Fact]
    public async Task Lint_Note_WhenIndexIsNotReady_ReturnsLoadingWithoutBridgeCall()
    {
        using var unreadyIndex = new VaultIndexService(
            NullLogger<VaultIndexService>.Instance,
            new KiokuConfiguration { VaultPath = _fixture.VaultPath });
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = 1 };
        using var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var tools = new PluginIntegrationTools(unreadyIndex, bridge);

        var result = await tools.lint("note");

        Assert.Equal("[loading] The index is still loading. Wait a moment and try again.", result);
    }

    private static async Task<JsonElement> ReceiveAndSucceedAsync(FakeObsidianServer server, string expectedCommand)
    {
        var socket = await server.AcceptAuthenticatedConnectionAsync();
        var raw = await server.ReceiveAsync(socket);
        var request = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(expectedCommand, request.GetProperty("command").GetString());

        var response = JsonSerializer.Serialize(new
        {
            requestId = request.GetProperty("requestId").GetString(),
            success = true,
            data = (object?)null,
            error = (string?)null,
            protocolVersion = 3
        });
        await server.SendAsync(socket, response);
        return request.Clone();
    }
}
