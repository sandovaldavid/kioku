using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for WorkflowTools.generate_digest. Each test gets its own temporary vault
/// (not shared via IClassFixture) since generate_digest writes a file and some tests backdate
/// note mtimes to exercise the day/week period cutoff.
/// </summary>
public class WorkflowToolsDigestTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;
    private TaskService _tasks = null!;
    private VaultConfigService _vaultConfig = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        _tasks = new TaskService(NullLogger<TaskService>.Instance, config);
        _vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private WorkflowTools CreateTools(GenerationService? generation = null)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var gen = generation ?? new GenerationService(
            config,
            NullLogger<GenerationService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        return new WorkflowTools(_fixture.Index, config, _tasks, _vaultConfig, gen, bridge);
    }

    private static string TodayFileName() => $"Digest {DateOnly.FromDateTime(DateTime.Now):yyyy-MM-dd}.md";

    [Fact]
    public async Task GenerateDigest_DryRun_ReturnsMarkdownWithoutWriting()
    {
        var tools = CreateTools();

        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("[info] Dry run", result);
        Assert.Contains("# Daily Digest", result);
        Assert.False(File.Exists(Path.Combine(_fixture.VaultPath, TodayFileName())));
    }

    [Fact]
    public async Task GenerateDigest_NoTasksInVault_TaskSectionsShowNothingToReport()
    {
        var tools = CreateTools();

        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("### Overdue", result);
        Assert.Contains("### Due soon", result);
        Assert.Contains("_Nothing to report._", result);
    }

    [Fact]
    public async Task GenerateDigest_OverdueAndDueSoonTasks_AppearInCorrectSections()
    {
        var dueSoon = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        await _fixture.CreateNoteAsync(
            "Task Note",
            $"- [ ] Overdue task 📅 2020-01-01\n- [ ] Due soon task 📅 {dueSoon:yyyy-MM-dd}\n- [x] Done task 📅 2020-01-01\n");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("Overdue task", result);
        Assert.Contains("Due soon task", result);
        Assert.DoesNotContain("Done task", result);
    }

    [Fact]
    public async Task GenerateDigest_OrphanNotes_ListsNotesWithNoLinks()
    {
        var tools = CreateTools();

        // The fixture's "Projects/Project Alpha" note has no outgoing links and no backlinks.
        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("## New orphaned notes", result);
        Assert.Contains("[[Project Alpha]]", result);
    }

    [Fact]
    public async Task GenerateDigest_DraftNote_AppearsInToReviewSection()
    {
        await _fixture.CreateNoteAsync("Draft Note", "Body.", status: "draft");
        await _fixture.Index.RebuildIndexAsync();
        var tools = CreateTools();

        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("## To review", result);
        Assert.Contains("[[Draft Note]] (status: draft)", result);
    }

    [Fact]
    public async Task GenerateDigest_ReRunSameDay_ReplacesNote()
    {
        var tools = CreateTools();

        var first = await tools.generate_digest();
        Assert.Contains("[ok] Digest generated", first);

        var second = await tools.generate_digest();
        Assert.Contains("[ok] Digest regenerated", second);

        var filePath = Path.Combine(_fixture.VaultPath, TodayFileName());
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task GenerateDigest_WeekPeriod_IncludesNoteExcludedByDayPeriod()
    {
        await _fixture.CreateNoteAsync("Old Note From Three Days Ago", "Body.");
        await _fixture.Index.RebuildIndexAsync();

        var oldPath = _fixture.GetNotePath("Old Note From Three Days Ago");
        var backdated = DateTime.UtcNow.AddDays(-3);
        File.SetLastWriteTimeUtc(oldPath, backdated);
        await _fixture.Index.RebuildIndexAsync();

        var tools = CreateTools();

        var dayDigest = await tools.generate_digest(period: "day", dry_run: true);
        var weekDigest = await tools.generate_digest(period: "week", dry_run: true);

        Assert.DoesNotContain("Old Note From Three Days Ago", dayDigest);
        Assert.Contains("Old Note From Three Days Ago", weekDigest);
    }

    [Fact]
    public async Task GenerateDigest_WithoutGenerationAvailable_NoSummarySectionButStillGenerates()
    {
        var tools = CreateTools();

        var result = await tools.generate_digest(dry_run: true);

        Assert.DoesNotContain("## Summary", result);
        Assert.Contains("## Activity", result);
    }

    [Fact]
    public async Task GenerateDigest_WithGenerationAvailable_IncludesSummarySection()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath, GenerationModel = "llama3.2" };
        var handler = new FakeHttpMessageHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { response = "It was a quiet period in the vault." }),
            });
        });
        var generation = new GenerationService(config, NullLogger<GenerationService>.Instance, new FakeHttpClientFactory(handler));
        await generation.InitializeAsync();
        var tools = CreateTools(generation);

        var result = await tools.generate_digest(dry_run: true);

        Assert.Contains("## Summary", result);
        Assert.Contains("It was a quiet period in the vault.", result);
    }

    [Fact]
    public async Task GenerateDigest_NoFoldersDailyConfigured_WritesToTargetFolderFallback()
    {
        var tools = CreateTools();

        var result = await tools.generate_digest(target_folder: "Custom");

        Assert.Contains("[ok] Digest generated", result);
        var expectedPath = Path.Combine(_fixture.VaultPath, "Custom", TodayFileName());
        Assert.True(File.Exists(expectedPath));
    }
}
