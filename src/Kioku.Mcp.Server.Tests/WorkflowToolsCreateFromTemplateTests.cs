using System.Net;
using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for WorkflowTools.create_note_from_template, in particular that the note is
/// immediately queryable (index-synced) right after creation — a follow-up update_frontmatter
/// call, as several prompts (write_daily, work_on_ticket) document, must not race the
/// FileSystemWatcher's debounce.
/// </summary>
public class WorkflowToolsCreateFromTemplateTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private (WorkflowTools Workflow, NoteCommandTools Commands) CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var tasks = new TaskService(NullLogger<TaskService>.Instance, config);
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var gen = new GenerationService(
            config,
            NullLogger<GenerationService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workflow = new WorkflowTools(_fixture.Index, config, tasks, vaultConfig, gen, bridge);
        var commands = new NoteCommandTools(_fixture.Index, config, vaultConfig);
        return (workflow, commands);
    }

    [Fact]
    public async Task CreateNoteFromTemplate_NoteIsImmediatelyQueryableInIndex()
    {
        var (workflow, _) = CreateTools();
        var templatesDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(Path.Combine(templatesDir, "daily.md"), "# Daily {{date}}", Encoding.UTF8);

        var result = await workflow.create_note_from_template("daily", "Daily/2026-07-14");

        Assert.StartsWith("[ok]", result);
        Assert.NotNull(_fixture.Index.GetNote(Path.Combine(_fixture.VaultPath, "Daily", "2026-07-14.md")));
    }

    [Fact]
    public async Task CreateNoteFromTemplate_ImmediateFollowUpUpdateFrontmatter_DoesNotRaceTheWatcher()
    {
        var (workflow, commands) = CreateTools();
        var templatesDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(Path.Combine(templatesDir, "daily.md"), "# Daily {{date}}", Encoding.UTF8);

        var createResult = await workflow.create_note_from_template("daily", "Daily/2026-07-14");
        Assert.StartsWith("[ok]", createResult);

        // No delay here on purpose — this is exactly the write_daily/work_on_ticket prompt flow:
        // create_note_from_template immediately followed by update_frontmatter in the same turn.
        var updateResult = await commands.update_frontmatter("Daily/2026-07-14", type: "daily", status: "active");

        Assert.StartsWith("[ok]", updateResult);
        var content = await File.ReadAllTextAsync(Path.Combine(_fixture.VaultPath, "Daily", "2026-07-14.md"));
        Assert.Contains("type: daily", content);
        Assert.Contains("status: active", content);
    }
}
