using System.Text.Json;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class WorkSessionCoordinationTests : IAsyncLifetime
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
    public async Task LegacySession_RemainsUncoordinatedAndReadable()
    {
        var services = CreateServices(enableCoordination: true);

        var result = await services.Sessions.StartAsync(
            sessionName: "Legacy session",
            sessionsFolder: "",
            goal: "Preserve compatibility.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null);

        var sessionId = GetSessionId(result);
        var note = FindById(sessionId);
        var metadata = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath)).ToFrontmatter();

        Assert.DoesNotContain("run_id", metadata.ExtraFields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("work_item_id", metadata.ExtraFields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("attempt_id", metadata.ExtraFields.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(Path.Combine(
            _fixture.VaultPath,
            ".kioku",
            "coordination",
            "events")));
        Assert.False(Directory.Exists(Path.Combine(
            _fixture.VaultPath,
            ".kioku",
            "coordination",
            "snapshots")));
        Assert.Contains("Legacy session", await File.ReadAllTextAsync(note.FilePath));
    }

    [Fact]
    public async Task LegacySession_CanBeLinkedWithContentCasBeforeClaimAcquisition()
    {
        var services = CreateServices(enableCoordination: true);
        var sessionId = GetSessionId(await services.Sessions.StartAsync(
            sessionName: "Linkable legacy session",
            sessionsFolder: "",
            goal: "Link this existing session.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null));
        var note = FindById(sessionId);
        var document = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath));
        document.SetString("custom_field", "preserve-me");
        document.ReplaceBody(document.Body + "\nManual edit before linking.\n");
        await File.WriteAllTextAsync(note.FilePath, document.Serialize(), NoteHelpers.Utf8NoBom);
        await _fixture.Index.SynchronizeFileReindexAsync(note.FilePath);

        var before = await File.ReadAllTextAsync(note.FilePath);
        var coordination = new WorkSessionCoordinationRequest("run-lazy", "work-lazy", "attempt-lazy");
        var rejected = await services.Sessions.StartAsync(
            sessionName: "",
            sessionsFolder: "",
            goal: "",
            project: "demo",
            agent: "codex",
            sessionId: sessionId,
            parentSessionId: "",
            mcpClientName: null,
            coordination: coordination);

        Assert.Contains("COORDINATED_SESSION_REQUIRES_PRECONDITIONS", rejected);
        Assert.Equal(before, await File.ReadAllTextAsync(note.FilePath));

        var linked = await services.Sessions.StartAsync(
            sessionName: "",
            sessionsFolder: "",
            goal: "",
            project: "demo",
            agent: "codex",
            sessionId: sessionId,
            parentSessionId: "",
            mcpClientName: null,
            coordination: coordination,
            preconditions: new VaultMutationPreconditions
            {
                ExpectedRevision = VaultRevision.Compute(before),
            });

        Assert.StartsWith("[ok]", linked);
        var metadata = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath)).ToFrontmatter();
        Assert.Equal("preserve-me", metadata.ExtraFields["custom_field"]);
        Assert.Equal("run-lazy", metadata.ExtraFields["run_id"]);
        Assert.Contains("Manual edit before linking.", await File.ReadAllTextAsync(note.FilePath));
        var projection = await services.Coordination.GetWorkItemAsync("run-lazy", "work-lazy");
        Assert.Equal(sessionId, projection.Projection.SessionId);
        Assert.Equal(CoordinationStates.Pending, projection.Projection.State);
    }

    [Fact]
    public async Task CoordinatedSession_CreatesLinkAndPreservesParentHandoff()
    {
        var services = CreateServices(enableCoordination: true);
        var parent = GetSessionId(await services.Sessions.StartAsync(
            sessionName: "Parent",
            sessionsFolder: "",
            goal: "Parent work.",
            project: "demo",
            agent: "claude",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null));

        var result = await services.Sessions.StartAsync(
            sessionName: "Coordinated child",
            sessionsFolder: "",
            goal: "Run coordinated work.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: parent,
            mcpClientName: "test-client",
            coordination: new WorkSessionCoordinationRequest("run-309", "work-309", "attempt-309"));

        var sessionId = GetSessionId(result);
        var note = FindById(sessionId);
        var metadata = FrontmatterDocument.Parse(await File.ReadAllTextAsync(note.FilePath)).ToFrontmatter();
        var relativePath = note.VaultRelativePath.Replace('\\', '/');

        Assert.Equal("run-309", metadata.ExtraFields["run_id"]);
        Assert.Equal("work-309", metadata.ExtraFields["work_item_id"]);
        Assert.Equal("attempt-309", metadata.ExtraFields["attempt_id"]);
        Assert.Contains("coordination", result);

        var projection = await services.Coordination.GetWorkItemAsync("run-309", "work-309");
        var handoff = await services.Coordination.GetHandoffPacketAsync("run-309", "work-309");

        Assert.Equal(sessionId, projection.Projection.SessionId);
        Assert.Equal(parent, projection.Projection.ParentSessionId);
        Assert.Equal(parent, handoff.ParentSessionId);
        Assert.Contains($"note:{relativePath}", projection.Projection.ResourceScope);
    }

    [Fact]
    public async Task CoordinatedSession_LegacyResumeAndEndRequireCasAndFence()
    {
        var services = CreateServices(enableCoordination: true);
        var sessionId = GetSessionId(await services.Sessions.StartAsync(
            sessionName: "Guarded session",
            sessionsFolder: "",
            goal: "Require coordinated mutation guards.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null,
            coordination: new WorkSessionCoordinationRequest("run-guard", "work-guard", "attempt-guard")));
        var note = FindById(sessionId);
        var before = await File.ReadAllTextAsync(note.FilePath);

        var resume = await services.Sessions.StartAsync(
            sessionName: "",
            sessionsFolder: "",
            goal: "",
            project: "demo",
            agent: "codex",
            sessionId: sessionId,
            parentSessionId: "",
            mcpClientName: null);
        var end = await services.Sessions.EndAsync(
            sessionNote: "",
            summary: "Attempted legacy close.",
            project: "demo",
            sessionId: sessionId,
            agent: "codex",
            mcpClientName: null);

        Assert.Contains("COORDINATED_SESSION_REQUIRES_PRECONDITIONS", resume);
        Assert.Contains("COORDINATED_SESSION_REQUIRES_PRECONDITIONS", end);
        Assert.Equal(before, await File.ReadAllTextAsync(note.FilePath));
        Assert.Equal("active", FindById(sessionId).Metadata.Status);

        var claim = await services.Coordination.AcquireClaimAsync(new CoordinationClaimAcquireRequest
        {
            RunId = "run-guard",
            WorkItemId = "work-guard",
            AttemptId = "attempt-guard",
            SessionId = sessionId,
            ResourceKey = $"note:{note.VaultRelativePath.Replace('\\', '/')}",
            TransitionId = "claim-guard",
            LeaseDuration = TimeSpan.FromMinutes(1),
            Agent = "codex",
        });
        var expectedRevision = VaultRevision.Compute(await File.ReadAllTextAsync(note.FilePath));

        var guardedEnd = await services.Sessions.EndAsync(
            sessionNote: "",
            summary: "Closed with current coordination preconditions.",
            project: "demo",
            sessionId: sessionId,
            agent: "codex",
            mcpClientName: null,
            preconditions: new VaultMutationPreconditions
            {
                ExpectedRevision = expectedRevision,
                ClaimId = claim.Claim.ClaimId,
                FenceGeneration = claim.Claim.FenceGeneration,
                ResourceKey = claim.Claim.ResourceKey,
            });

        Assert.StartsWith("[ok]", guardedEnd);
        Assert.Equal("done", FindById(sessionId).Metadata.Status);
    }

    [Fact]
    public async Task CoordinationLink_WhenCapabilityIsDisabledDoesNotCreateSession()
    {
        var services = CreateServices(enableCoordination: false);

        var result = await services.Sessions.StartAsync(
            sessionName: "Rejected link",
            sessionsFolder: "",
            goal: "The capability is disabled.",
            project: "demo",
            agent: "codex",
            sessionId: "",
            parentSessionId: "",
            mcpClientName: null,
            coordination: new WorkSessionCoordinationRequest("run-disabled", "work-disabled", "attempt-disabled"));

        Assert.Contains("COORDINATION_DISABLED", result);
        Assert.DoesNotContain(
            _fixture.Index.GetAllNotes(),
            note => note.Metadata.NoteType == "session");
    }

    private (WorkSessionService Sessions, CoordinationService Coordination) CreateServices(
        bool enableCoordination)
    {
        if (enableCoordination)
        {
            Directory.CreateDirectory(Path.Combine(_fixture.VaultPath, ".kioku"));
            File.WriteAllText(
                Path.Combine(_fixture.VaultPath, ".kioku", "config.yml"),
                "capabilities:\n  enabled: [coordination]\n");
        }

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        var paths = new VaultPathPolicy(config);
        var fileSystem = new CoordinationFileSystem();
        var validator = new CoordinationContractValidator();
        var events = new CoordinationEventStore(paths, fileSystem, validator, _time);
        var claims = new CoordinationClaimStore(paths, fileSystem, events, validator, _time);
        var conflicts = new CoordinationConflictStore(paths, fileSystem, validator, _time);
        var coordination = new CoordinationService(events, claims, conflicts, _time);
        var mutations = new VaultMutationService(
            paths,
            fileSystem,
            claims,
            new VaultIndexOperations(_fixture.Index),
            _time);
        var sessions = new WorkSessionService(
            _fixture.Index,
            config,
            vaultConfig,
            workspace,
            bridge,
            new WorkSessionFileSystem(),
            _time,
            mutations,
            coordination);
        return (sessions, coordination);
    }

    private Note FindById(string sessionId) =>
        _fixture.Index.GetAllNotes().Single(note =>
            note.Metadata.ExtraFields.GetValueOrDefault("session_id") == sessionId);

    private static string GetSessionId(string result)
    {
        var json = result.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Last(line => line.StartsWith('{'));
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("session_id").GetString()!;
    }
}
