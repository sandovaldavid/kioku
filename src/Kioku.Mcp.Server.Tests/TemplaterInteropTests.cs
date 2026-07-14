using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Templater bridge interop: rendered note bodies containing Templater's &lt;% %&gt; syntax get
/// evaluated in place by the real Templater plugin (via the Obsidian bridge) after the server
/// writes the note. When Templater/Obsidian isn't reachable, note creation still succeeds and a
/// warning is appended — this is best-effort, never a hard failure.
/// </summary>
public class TemplaterInteropTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    // No server listens on port 1 (a privileged port); connecting to it as a client needs no
    // elevation and fails fast, exercising the same graceful-degradation path as a closed port.
    private static ObsidianBridgeService CreateUnreachableBridge(KiokuConfiguration config) =>
        new(NullLogger<ObsidianBridgeService>.Instance,
            new KiokuConfiguration { VaultPath = config.VaultPath, ObsidianBridgePort = 1 });

    // Detection

    [Fact]
    public async Task EvaluateTemplaterInPlaceAsync_PlainContent_ReturnsNotNeededWithoutAnyRpcAttempt()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var bridge = CreateUnreachableBridge(config);

        var result = await bridge.EvaluateTemplaterInPlaceAsync("# Title\n\nJust {{date}} and markdown.", "note.md");

        Assert.Equal(TemplaterEvaluationResult.NotNeeded, result);
    }

    [Fact]
    public async Task EvaluateTemplaterInPlaceAsync_SimpleTag_AttemptsRpcAndDegradesGracefully()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var bridge = CreateUnreachableBridge(config);

        var result = await bridge.EvaluateTemplaterInPlaceAsync("Today is <% tp.date.now() %>.", "note.md");

        Assert.False(result.Applied);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task EvaluateTemplaterInPlaceAsync_MultilineStarBlock_AttemptsRpcAndDegradesGracefully()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var bridge = CreateUnreachableBridge(config);

        var result = await bridge.EvaluateTemplaterInPlaceAsync(
            "<%*\nlet x = 1;\ntR += x;\n%>", "note.md");

        Assert.False(result.Applied);
        Assert.NotNull(result.Warning);
    }

    // Doc creation integration

    private (EngineeringWorkflowTools Tools, ProjectWorkspaceService Workspace) CreateTools(ObsidianBridgeService bridge)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        return (new EngineeringWorkflowTools(_fixture.Index, config, vaultConfig, workspace, bridge), workspace);
    }

    [Fact]
    public async Task RecordAdr_NoBridgeAvailable_SucceedsWithWarningAndLeavesSyntaxIntact()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var bridge = CreateUnreachableBridge(config);
        var (tools, workspace) = CreateTools(bridge);

        var templatesDir = Path.Combine(_fixture.VaultPath, "Templates", "kioku");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "adr.md"),
            "# {{title}}\n\nDecided on <% tp.date.now(\"YYYY-MM-DD\") %>: {{decision}}",
            Encoding.UTF8);

        var result = await tools.record_adr("demo", "Use SQLite", "ctx", "the decision", "cons");

        Assert.StartsWith("[ok]", result);
        Assert.Contains("[warning] template contains Templater syntax; left unevaluated", result);

        var files = Directory.GetFiles(workspace.GetSubfolder("demo", "decisions"), "ADR-*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("<% tp.date.now(\"YYYY-MM-DD\") %>", content);
    }

    [Fact]
    public async Task RecordAdr_PlainVarTemplate_NoWarningAndNoRpcNeeded()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var bridge = CreateUnreachableBridge(config);
        var (tools, _) = CreateTools(bridge);

        var result = await tools.record_adr("demo", "Use SQLite", "ctx", "the decision", "cons");

        Assert.StartsWith("[ok]", result);
        Assert.DoesNotContain("[warning]", result);
    }

    [Fact]
    public async Task RecordAdr_TemplaterEvaluationSucceeds_ReindexesAndOmitsWarning()
    {
        await using var server = await FakeObsidianServer.StartAsync();
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, ObsidianBridgePort = server.Port };
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var (tools, workspace) = CreateTools(bridge);

        var templatesDir = Path.Combine(_fixture.VaultPath, "Templates", "kioku");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "adr.md"),
            "# {{title}}\n\nDecided on <% tp.date.now(\"YYYY-MM-DD\") %>: {{decision}}",
            Encoding.UTF8);

        var serverSide = Task.Run(async () =>
        {
            var socket = await server.AcceptAuthenticatedConnectionAsync();
            var raw = await server.ReceiveAsync(socket);
            var message = JsonDocument.Parse(raw).RootElement;

            Assert.Equal("evaluate-templater-in-file", message.GetProperty("command").GetString());
            var requestId = message.GetProperty("requestId").GetString();
            var notePath = message.GetProperty("payload").GetProperty("notePath").GetString()!;

            var response = JsonSerializer.Serialize(new
            {
                requestId,
                success = true,
                data = new { path = notePath },
                error = (string?)null,
                protocolVersion = 2,
            });
            await server.SendAsync(socket, response);
        });

        var result = await tools.record_adr("demo", "Use SQLite", "ctx", "the decision", "cons");
        await serverSide;

        Assert.StartsWith("[ok]", result);
        Assert.DoesNotContain("[warning]", result);
    }
}
