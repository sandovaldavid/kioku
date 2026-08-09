using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Prompts;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Regression coverage for optional project workflow folders. daily/ and tickets/ remain
/// recognized project categories, but the project scaffold must not materialize them until an
/// explicit note write targets the configured folder.
/// </summary>
public class ProjectOptionalFoldersTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static ObsidianBridgeService CreateBridge(KiokuConfiguration config) =>
        new(NullLogger<ObsidianBridgeService>.Instance, config);

    private (EngineeringWorkflowTools Tools, ProjectWorkspaceService Workspace, NoteCommandTools Notes) CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = CreateBridge(config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        var documents = new ProjectDocumentService(
            _fixture.Index,
            config,
            vaultConfig,
            workspace,
            bridge,
            new ProjectDocumentFileSystem());
        var notes = new NoteCommandTools(_fixture.Index, config, vaultConfig);
        return (new EngineeringWorkflowTools(documents), workspace, notes);
    }

    [Fact]
    public async Task NewProjectScaffold_CreatesCoreFoldersButLeavesOptionalFoldersAbsent()
    {
        var (_, workspace, _) = CreateTools();

        await workspace.EnsureProjectScaffoldAsync("demo");

        foreach (var key in ProjectWorkspaceService.CoreSubfolderKeys)
        {
            Assert.True(Directory.Exists(workspace.GetSubfolder("demo", key)), $"missing core subfolder '{key}'");
        }

        foreach (var key in ProjectWorkspaceService.OptionalSubfolderKeys)
        {
            Assert.False(Directory.Exists(workspace.GetSubfolder("demo", key)), $"optional subfolder '{key}' must be lazy");
        }

        Assert.True(File.Exists(Path.Combine(workspace.GetProjectFolder("demo"), "demo.md")));
        Assert.True(File.Exists(workspace.GetVaultTemplatePath("daily")));
        Assert.True(File.Exists(workspace.GetVaultTemplatePath("ticket")));
    }

    [Fact]
    public async Task ExistingOptionalFoldersAndNotes_RemainUntouchedByScaffold()
    {
        var (_, workspace, _) = CreateTools();
        var daily = workspace.GetSubfolder("demo", "daily");
        var tickets = workspace.GetSubfolder("demo", "tickets");
        Directory.CreateDirectory(daily);
        Directory.CreateDirectory(tickets);
        var dailyNote = Path.Combine(daily, "2026-08-08.md");
        var ticketNote = Path.Combine(tickets, "T-1.md");
        await File.WriteAllTextAsync(dailyNote, "historical daily", Encoding.UTF8);
        await File.WriteAllTextAsync(ticketNote, "historical ticket", Encoding.UTF8);

        await workspace.EnsureProjectScaffoldAsync("demo");
        await workspace.EnsureProjectScaffoldAsync("demo");

        Assert.Equal("historical daily", await File.ReadAllTextAsync(dailyNote));
        Assert.Equal("historical ticket", await File.ReadAllTextAsync(ticketNote));
        Assert.True(Directory.Exists(daily));
        Assert.True(Directory.Exists(tickets));
    }

    [Fact]
    public async Task GroupedProjectAndCustomFolders_KeepOptionalFoldersLazy()
    {
        var configPath = Path.Combine(_fixture.VaultPath, ".kioku", "config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            """
            engineering:
              subfolders:
                decisions: adrs
                daily: journal
                tickets: work-items
            """,
            Encoding.UTF8);
        var (_, workspace, _) = CreateTools();

        await workspace.EnsureProjectScaffoldAsync("Atena/api.core");

        Assert.True(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "decisions")));
        Assert.EndsWith(Path.Combine("Atena", "api.core", "adrs"), workspace.GetSubfolder("Atena/api.core", "decisions"));
        Assert.EndsWith(Path.Combine("Atena", "api.core", "journal"), workspace.GetSubfolder("Atena/api.core", "daily"));
        Assert.EndsWith(Path.Combine("Atena", "api.core", "work-items"), workspace.GetSubfolder("Atena/api.core", "tickets"));
        Assert.False(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "daily")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("Atena/api.core", "tickets")));
        Assert.True(File.Exists(Path.Combine(workspace.GetProjectFolder("Atena/api.core"), "api.core.md")));
    }

    [Fact]
    public async Task ConcurrentScaffold_IsIdempotentAndDoesNotCreateOptionalFolders()
    {
        var (_, workspace, _) = CreateTools();

        await Task.WhenAll(
            workspace.EnsureProjectScaffoldAsync("demo"),
            workspace.EnsureProjectScaffoldAsync("demo"));

        foreach (var key in ProjectWorkspaceService.CoreSubfolderKeys)
        {
            Assert.True(Directory.Exists(workspace.GetSubfolder("demo", key)));
        }

        Assert.All(
            ProjectWorkspaceService.OptionalSubfolderKeys,
            key => Assert.False(Directory.Exists(workspace.GetSubfolder("demo", key))));
        Assert.Single(Directory.GetFiles(workspace.GetProjectFolder("demo"), "demo.md", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Daily_FirstExplicitWriteCreatesFolderLazily_SecondWriteReusesIt()
    {
        var (_, workspace, notes) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var dailyFolder = workspace.GetSubfolder("demo", "daily");
        var relativeDailyFolder = workspace.ToVaultRelative(dailyFolder);
        Assert.False(Directory.Exists(dailyFolder));

        var first = await notes.create_note(
            name: "2026-08-09",
            content: "first daily",
            folder: relativeDailyFolder,
            type: "daily",
            status: "active",
            tags: "daily");
        var second = await notes.create_note(
            name: "2026-08-10",
            content: "second daily",
            folder: relativeDailyFolder,
            type: "daily",
            status: "active",
            tags: "daily");

        Assert.StartsWith("[ok]", first);
        Assert.StartsWith("[ok]", second);
        Assert.True(Directory.Exists(dailyFolder));
        Assert.True(File.Exists(Path.Combine(dailyFolder, "2026-08-09.md")));
        Assert.True(File.Exists(Path.Combine(dailyFolder, "2026-08-10.md")));
    }

    [Fact]
    public async Task Daily_ConcurrentFirstWritesCreateParentSafely()
    {
        var (_, workspace, notes) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var dailyFolder = workspace.GetSubfolder("demo", "daily");
        var relativeDailyFolder = workspace.ToVaultRelative(dailyFolder);
        Assert.False(Directory.Exists(dailyFolder));

        var results = await Task.WhenAll(
            notes.create_note("2026-08-09-a", "a", folder: relativeDailyFolder, type: "daily"),
            notes.create_note("2026-08-09-b", "b", folder: relativeDailyFolder, type: "daily"));

        Assert.All(results, result => Assert.StartsWith("[ok]", result));
        Assert.Equal(2, Directory.GetFiles(dailyFolder, "*.md", SearchOption.TopDirectoryOnly).Length);
    }

    [Fact]
    public async Task Ticket_ExplicitLocalWriteCreatesFolderLazily()
    {
        var (_, workspace, notes) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var ticketsFolder = workspace.GetSubfolder("demo", "tickets");
        Assert.False(Directory.Exists(ticketsFolder));

        var result = await notes.create_note(
            name: "T-42",
            content: "human-authored requirements",
            folder: workspace.ToVaultRelative(ticketsFolder),
            type: "ticket",
            status: "open");

        Assert.StartsWith("[ok]", result);
        Assert.True(Directory.Exists(ticketsFolder));
        Assert.Contains("human-authored requirements", await File.ReadAllTextAsync(Path.Combine(ticketsFolder, "T-42.md")));
    }

    [Fact]
    public async Task ProjectContext_AcceptsOptionalAliasesWhenFoldersAreAbsent_WithoutCreatingThem()
    {
        var (tools, workspace, _) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var dailyFolder = workspace.GetSubfolder("demo", "daily");
        var ticketsFolder = workspace.GetSubfolder("demo", "tickets");

        var daily = await tools.get_project_context("demo", types: "daily");
        var ticket = await tools.get_project_context("demo", types: "ticket");
        var tickets = await tools.get_project_context("demo", types: "tickets");
        var all = await tools.get_project_context("demo");

        Assert.Contains("Daily (0)", daily);
        Assert.Contains("Tickets (0)", ticket);
        Assert.Contains("Tickets (0)", tickets);
        Assert.Contains("Daily (0)", all);
        Assert.Contains("Tickets (0)", all);
        Assert.False(Directory.Exists(dailyFolder));
        Assert.False(Directory.Exists(ticketsFolder));
    }

    [Fact]
    public async Task ProjectContext_DiscoversExistingOptionalNotesAndKeepsAliasesEquivalent()
    {
        var (tools, workspace, notes) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var dailyFolder = workspace.GetSubfolder("demo", "daily");
        var ticketsFolder = workspace.GetSubfolder("demo", "tickets");
        await notes.create_note("2026-08-09", "daily body", folder: workspace.ToVaultRelative(dailyFolder), type: "daily");
        await notes.create_note("T-7", "ticket body", folder: workspace.ToVaultRelative(ticketsFolder), type: "ticket");

        var daily = await tools.get_project_context("demo", types: "daily", include_content: true);
        var ticket = await tools.get_project_context("demo", types: "ticket", include_content: true);
        var tickets = await tools.get_project_context("demo", types: "tickets", include_content: true);

        Assert.Contains("2026-08-09", daily);
        Assert.Contains("daily body", daily);
        Assert.Contains("T-7", ticket);
        Assert.Contains("ticket body", ticket);
        Assert.Contains("T-7", tickets);
        Assert.Contains("ticket body", tickets);
    }

    [Fact]
    public async Task TemplaterMappings_IncludeFutureOptionalFoldersWithoutCreatingThem()
    {
        var settingsPath = Path.Combine(
            _fixture.VaultPath,
            ".obsidian",
            "plugins",
            "templater-obsidian",
            "data.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        await File.WriteAllTextAsync(
            settingsPath,
            """{ "enable_folder_templates": false, "folder_templates": [] }""",
            Encoding.UTF8);
        var (_, workspace, _) = CreateTools();

        await workspace.EnsureProjectScaffoldAsync("demo");

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var mappings = json.RootElement.GetProperty("folder_templates")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("folder").GetString()!,
                item => item.GetProperty("template").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        var dailyFolder = workspace.ToVaultRelative(workspace.GetSubfolder("demo", "daily"));
        var ticketsFolder = workspace.ToVaultRelative(workspace.GetSubfolder("demo", "tickets"));

        Assert.Contains(dailyFolder, mappings.Keys);
        Assert.Contains(ticketsFolder, mappings.Keys);
        Assert.EndsWith("/kioku/daily.md", mappings[dailyFolder].Replace('\\', '/'));
        Assert.EndsWith("/kioku/ticket.md", mappings[ticketsFolder].Replace('\\', '/'));
        Assert.False(Directory.Exists(workspace.GetSubfolder("demo", "daily")));
        Assert.False(Directory.Exists(workspace.GetSubfolder("demo", "tickets")));
    }

    [Fact]
    public async Task TemplaterMapping_DoesNotOverwriteUserMappingForOptionalFolder()
    {
        var settingsPath = Path.Combine(
            _fixture.VaultPath,
            ".obsidian",
            "plugins",
            "templater-obsidian",
            "data.json");
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var (_, workspace, _) = CreateTools();
        var dailyFolder = workspace.ToVaultRelative(workspace.GetSubfolder("demo", "daily"));
        await File.WriteAllTextAsync(
            settingsPath,
            $$"""{ "enable_folder_templates": true, "folder_templates": [{ "folder": "{{dailyFolder}}", "template": "Templates/custom-daily.md" }] }""",
            Encoding.UTF8);

        await workspace.EnsureProjectScaffoldAsync("demo");

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(settingsPath));
        var mapping = json.RootElement.GetProperty("folder_templates")
            .EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("folder").GetString(), dailyFolder, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Templates/custom-daily.md", mapping.GetProperty("template").GetString());
        Assert.False(Directory.Exists(workspace.GetSubfolder("demo", "daily")));
    }

    [Fact]
    public async Task WorkOnTicketPrompt_ReadsExistingTicketBeforeAnyMutation_AndMissingLookupDoesNotScaffoldTickets()
    {
        var (_, workspace, _) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");
        var ticketsFolder = workspace.GetSubfolder("demo", "tickets");
        Assert.False(Directory.Exists(ticketsFolder));

        var prompt = KiokuPrompts.work_on_ticket("demo", "missing-ticket");

        var readIndex = prompt.IndexOf("`read_note`", StringComparison.Ordinal);
        var editIndex = prompt.IndexOf("`edit_note`", StringComparison.Ordinal);
        var planIndex = prompt.IndexOf("`create_project_doc`", StringComparison.Ordinal);
        Assert.True(readIndex >= 0);
        Assert.True(editIndex > readIndex);
        Assert.True(planIndex > readIndex);
        Assert.False(Directory.Exists(ticketsFolder));
    }
}
