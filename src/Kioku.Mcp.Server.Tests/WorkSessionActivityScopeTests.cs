using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Regression coverage for GitHub #438: a session's "notes touched"/activity summary must not
/// leak concurrent edits made to a different project by another agent or user. Both reporting
/// paths (end_work_session and list_work_sessions include_activity=true) route through the same
/// WorkSessionService.GetProjectScopedNotes helper.
/// </summary>
public sealed class WorkSessionActivityScopeTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private ManualTimeProvider _time = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
        _time = new ManualTimeProvider(new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task EndWorkSession_DoesNotListNotesFromAnotherProjectAsTouched()
    {
        var tools = CreateTools();
        var startResult = await tools.start_work_session(project: "alpha", agent: "claude");
        var sessionId = GetString(startResult, "session_id");
        var sessionPath = GetString(startResult, "path");

        _time.Advance(TimeSpan.FromMinutes(5));
        await WriteNoteAsync("alpha", "Alpha Note", touchedAt: _time.GetUtcNow());
        await WriteNoteAsync("beta", "Beta Note", touchedAt: _time.GetUtcNow());
        await _fixture.Index.RebuildIndexAsync();

        _time.Advance(TimeSpan.FromMinutes(5));
        var result = await tools.end_work_session(project: "alpha", session_id: sessionId);

        Assert.StartsWith("[ok] Session closed", result);

        // BuildEndBlock writes the "Notes touched during session" list into the session note's
        // own body on disk, not into the tool's returned summary — read it back to check exactly
        // what the issue's reproduction steps describe.
        var sessionBody = await File.ReadAllTextAsync(Path.Combine(_fixture.VaultPath, sessionPath));
        Assert.Contains("Alpha Note", sessionBody);
        Assert.DoesNotContain("Beta Note", sessionBody);
    }

    [Fact]
    public async Task ListWorkSessions_ActivityDoesNotListNotesFromAnotherProject()
    {
        var tools = CreateTools();
        await tools.start_work_session(project: "alpha", agent: "claude");

        _time.Advance(TimeSpan.FromMinutes(5));
        await WriteNoteAsync("alpha", "Alpha Note", touchedAt: _time.GetUtcNow());
        await WriteNoteAsync("beta", "Beta Note", touchedAt: _time.GetUtcNow());
        await _fixture.Index.RebuildIndexAsync();

        var result = await tools.list_work_sessions(project: "alpha", include_activity: true);

        Assert.Contains("Alpha Note", result);
        Assert.DoesNotContain("Beta Note", result);
    }

    private async Task WriteNoteAsync(string project, string noteName, DateTimeOffset touchedAt)
    {
        var folder = GetWorkspace().GetProjectFolder(project);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{noteName}.md");
        await File.WriteAllTextAsync(path, $"---\ntype: note\n---\nBody of {noteName}.", Encoding.UTF8);
        File.SetLastWriteTimeUtc(path, touchedAt.UtcDateTime);
    }

    private SessionContextTools CreateTools() => new(CreateService());

    private WorkSessionService CreateService()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        return new WorkSessionService(
            _fixture.Index,
            config,
            vaultConfig,
            workspace,
            bridge,
            new WorkSessionFileSystem(),
            _time);
    }

    private ProjectWorkspaceService GetWorkspace()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        return new ProjectWorkspaceService(config, vaultConfig, bridge);
    }

    private static string GetString(string result, string property)
    {
        var json = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line.StartsWith('{'));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty(property).GetString()!;
    }
}
