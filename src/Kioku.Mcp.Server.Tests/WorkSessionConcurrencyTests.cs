using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class WorkSessionConcurrencyTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private ManualTimeProvider _time = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
        _time = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 18, 20, 0, 0, TimeSpan.Zero));
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task ApplicationService_StartAndEnd_DoNotRequireMcpHost()
    {
        var service = CreateService();
        var started = await service.StartAsync(
            sessionName: "",
            sessionsFolder: "",
            goal: "Validate the application boundary.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null);
        var sessionId = GetSessionId(started);

        _time.Advance(TimeSpan.FromMinutes(10));
        var ended = await service.EndAsync(
            sessionNote: "",
            summary: "Application service completed without an MCP host.",
            project: "demo",
            sessionId: sessionId,
            agent: "codex",
            mcpClientName: null);

        Assert.StartsWith("[ok]", ended);
        Assert.Contains("\"duration_seconds\":600", ended);
        Assert.Equal("done", FindById(sessionId).Metadata.Status);
    }

    [Fact]
    public async Task CancelledToolCall_DoesNotCreateSessionFile()
    {
        var tools = CreateTools();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tools.start_work_session(
                project: "demo",
                agent: "codex",
                cancellationToken: cancellation.Token));

        Assert.False(Directory.Exists(GetWorkspace().GetSubfolder("demo", "sessions")));
    }

    [Fact]
    public async Task ParallelStarts_ThreeAgents_GetDistinctIdsAndFiles()
    {
        var tools = CreateTools();
        var results = await Task.WhenAll(
            tools.start_work_session(project: "demo", agent: "claude"),
            tools.start_work_session(project: "demo", agent: "codex"),
            tools.start_work_session(project: "demo", agent: "opencode"));

        var ids = results.Select(GetSessionId).ToList();
        Assert.Equal(3, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        var sessionsFolder = GetWorkspace().GetSubfolder("demo", "sessions");
        var files = Directory.GetFiles(sessionsFolder, "*.md");
        Assert.Equal(3, files.Length);

        foreach (var file in files)
        {
            var document = FrontmatterDocument.Parse(await File.ReadAllTextAsync(file));
            var metadata = document.ToFrontmatter();
            Assert.Equal("active", metadata.Status);
            Assert.Equal("demo", metadata.ExtraFields["project"]);
            Assert.NotNull(metadata.ExtraFields["session_id"]);
            Assert.EndsWith("Z", Assert.IsType<string>(metadata.ExtraFields["started_at"]));
        }
    }

    [Fact]
    public async Task ImplicitEnd_WithMultipleAgents_ReturnsCandidatesInsteadOfChoosingLatest()
    {
        var tools = CreateTools();
        await tools.start_work_session(project: "demo", agent: "claude");
        await tools.start_work_session(project: "demo", agent: "codex");

        var result = await tools.end_work_session(project: "demo");

        Assert.StartsWith("[error]", result);
        Assert.Contains("AMBIGUOUS_SESSION", result);
        Assert.Contains("session_id", result);

        await _fixture.Index.RebuildIndexAsync();
        Assert.Equal(2, ActiveProjectSessions("demo").Count);
    }

    [Fact]
    public async Task ImplicitEnd_WithAgent_ClosesOnlyThatAgentsSession()
    {
        var tools = CreateTools();
        var claude = GetSessionId(await tools.start_work_session(project: "demo", agent: "claude"));
        var codex = GetSessionId(await tools.start_work_session(project: "demo", agent: "codex"));

        var result = await tools.end_work_session(
            project: "demo",
            agent: "claude",
            summary: "Claude completed its work.");

        Assert.StartsWith("[ok]", result);
        await _fixture.Index.RebuildIndexAsync();

        Assert.Equal("done", FindById(claude).Metadata.Status);
        Assert.Equal("active", FindById(codex).Metadata.Status);
    }

    [Fact]
    public async Task ParallelEnds_BySessionId_CloseDistinctSessions()
    {
        var tools = CreateTools();
        var ids = await Task.WhenAll(
            tools.start_work_session(project: "demo", agent: "claude"),
            tools.start_work_session(project: "demo", agent: "codex"),
            tools.start_work_session(project: "demo", agent: "opencode"));
        var sessionIds = ids.Select(GetSessionId).ToArray();

        _time.Advance(TimeSpan.FromMinutes(15));
        var results = await Task.WhenAll(sessionIds.Select(id =>
            tools.end_work_session(
                session_id: id,
                summary: $"Closed {id}.")));

        Assert.All(results, result => Assert.StartsWith("[ok]", result));
        await _fixture.Index.RebuildIndexAsync();
        Assert.All(sessionIds, id => Assert.Equal("done", FindById(id).Metadata.Status));
    }

    [Fact]
    public async Task RestartedTool_ResumesExistingSessionById()
    {
        var firstTools = CreateTools();
        var sessionId = GetSessionId(
            await firstTools.start_work_session(project: "demo", agent: "codex"));

        await _fixture.Index.RebuildIndexAsync();
        _time.Advance(TimeSpan.FromMinutes(5));
        var restartedTools = CreateTools();

        var result = await restartedTools.start_work_session(
            project: "demo",
            session_id: sessionId);

        Assert.StartsWith("[ok]", result);
        Assert.Contains("\"action\":\"resumed\"", result);

        var note = FindById(sessionId);
        var content = await File.ReadAllTextAsync(note.FilePath);
        Assert.Contains("## Session resumed", content);
        Assert.Equal("active", note.Metadata.Status);
    }

    [Fact]
    public async Task End_UsesPersistedUtcTimestampAndPreservesManualEdits()
    {
        var tools = CreateTools();
        var sessionId = GetSessionId(
            await tools.start_work_session(project: "demo", agent: "claude"));

        var note = FindById(sessionId);
        var document = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath));
        document.SetString("custom_field", "keep-me");
        document.ReplaceBody(document.Body + "\nManual Obsidian edit.\n");
        await File.WriteAllTextAsync(note.FilePath, document.Serialize(), NoteHelpers.Utf8NoBom);
        File.SetLastWriteTimeUtc(note.FilePath, DateTime.UtcNow.AddDays(-10));
        await _fixture.Index.SynchronizeFileReindexAsync(note.FilePath);

        _time.Advance(TimeSpan.FromMinutes(90));
        var result = await tools.end_work_session(
            session_id: sessionId,
            summary: "Finished safely.");

        Assert.Contains("\"duration_seconds\":5400", result);

        var closed = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath));
        var metadata = closed.ToFrontmatter();
        Assert.Equal("done", metadata.Status);
        Assert.Equal("keep-me", metadata.ExtraFields["custom_field"]);
        Assert.Equal("2026-07-18T20:00:00.0000000Z", metadata.ExtraFields["started_at"]);
        Assert.Equal("2026-07-18T21:30:00.0000000Z", metadata.ExtraFields["ended_at"]);
        Assert.Contains("Manual Obsidian edit.", closed.Body);
        Assert.Contains("Finished safely.", closed.Body);
    }

    [Fact]
    public async Task ParentSessionId_IsPersistedForHandoffChains()
    {
        var tools = CreateTools();
        var parentId = GetSessionId(
            await tools.start_work_session(project: "demo", agent: "claude"));

        var childId = GetSessionId(await tools.start_work_session(
            project: "demo",
            agent: "codex",
            parent_session_id: parentId));

        var child = FindById(childId);
        Assert.Equal(parentId, child.Metadata.ExtraFields["parent_session_id"]);
    }

    private SessionContextTools CreateTools() => new(CreateService());

    private WorkSessionService CreateService()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(
            config,
            NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(
            NullLogger<ObsidianBridgeService>.Instance,
            config);
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
        var vaultConfig = new VaultConfigService(
            config,
            NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(
            NullLogger<ObsidianBridgeService>.Instance,
            config);
        return new ProjectWorkspaceService(config, vaultConfig, bridge);
    }

    private List<Kioku.Mcp.Server.Domain.Note> ActiveProjectSessions(string project) =>
        _fixture.Index.GetAllNotes()
            .Where(note =>
                note.Metadata.NoteType == "session" &&
                note.Metadata.Status == "active" &&
                note.Metadata.ExtraFields.GetValueOrDefault("project") == project)
            .ToList();

    private Kioku.Mcp.Server.Domain.Note FindById(string id) =>
        _fixture.Index.GetAllNotes().Single(note =>
            note.Metadata.ExtraFields.GetValueOrDefault("session_id") == id);

    private static string GetSessionId(string result)
    {
        var json = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line.StartsWith('{'));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("session_id").GetString()!;
    }
}
