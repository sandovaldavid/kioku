using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for the engineering tool group: per-project ADRs, bugs, plans,
/// knowledge, backlog, project context re-reading, and workspace scaffolding.
/// Each test gets its own temporary vault since these operations mutate files on disk.
/// </summary>
public class EngineeringWorkflowToolsTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>
    /// A bridge with no live Obsidian listening on the other end. SendRequestAsync degrades
    /// gracefully (Success=false, no throw) so tests can exercise the Templater-interop code
    /// path without a real plugin connection.
    /// </summary>
    private static ObsidianBridgeService CreateBridge(KiokuConfiguration config) =>
        new(NullLogger<ObsidianBridgeService>.Instance, config);

    private (EngineeringWorkflowTools Tools, ProjectWorkspaceService Workspace) CreateTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = CreateBridge(config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        return (new EngineeringWorkflowTools(_fixture.Index, config, vaultConfig, workspace, bridge), workspace);
    }

    private SessionContextTools CreateSessionTools()
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = CreateBridge(config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        return new SessionContextTools(_fixture.Index, config, vaultConfig, workspace, bridge);
    }

    // ADR numbering

    [Fact]
    public async Task RecordAdr_AssignsSequentialNumbers()
    {
        var (tools, _) = CreateTools();

        var first = await tools.record_adr("demo", "Use SQLite", "ctx", "decision", "consequences");
        var second = await tools.record_adr("demo", "Use WAL mode", "ctx", "decision", "consequences");

        Assert.Contains("ADR-0001-Use-SQLite", first);
        Assert.Contains("ADR-0002-Use-WAL-mode", second);
    }

    [Fact]
    public async Task RecordAdr_NumberingSkipsGapsToMaxPlusOne()
    {
        var (tools, workspace) = CreateTools();
        await workspace.EnsureProjectScaffoldAsync("demo");

        var decisions = workspace.GetSubfolder("demo", "decisions");
        await File.WriteAllTextAsync(Path.Combine(decisions, "ADR-0007-manual.md"), "# manual", Encoding.UTF8);

        var result = await tools.record_adr("demo", "Next one", "ctx", "d", "c");

        Assert.Contains("ADR-0008-Next-one", result);
    }

    [Fact]
    public async Task RecordAdr_InvalidStatus_ReturnsErrorWithOptions()
    {
        var (tools, _) = CreateTools();

        var result = await tools.record_adr("demo", "T", "c", "d", "q", status: "banana");

        Assert.StartsWith("[error]", result);
        Assert.Contains("proposed", result);
        Assert.Contains("superseded", result);
    }

    [Fact]
    public async Task RecordAdr_InvalidProjectName_ReturnsError()
    {
        var (tools, _) = CreateTools();

        Assert.StartsWith("[error]", await tools.record_adr("a/b", "T", "c", "d", "q"));
        Assert.StartsWith("[error]", await tools.record_adr("", "T", "c", "d", "q"));
    }

    // Scaffolding

    [Fact]
    public async Task FirstWrite_ScaffoldsProjectStructureAndMoc()
    {
        var (tools, workspace) = CreateTools();

        await tools.log_bug("demo", "Crash on start", "sym", "cause", "fix");

        foreach (var key in ProjectWorkspaceService.SubfolderKeys)
        {
            Assert.True(Directory.Exists(workspace.GetSubfolder("demo", key)), $"missing subfolder '{key}'");
        }

        var moc = Path.Combine(workspace.GetProjectFolder("demo"), "demo.md");
        Assert.True(File.Exists(moc));
        var mocContent = await File.ReadAllTextAsync(moc);
        Assert.Contains("type: moc", mocContent);
        Assert.Contains("project: demo", mocContent);
    }

    [Fact]
    public async Task SetupAgentWorkflow_IsIdempotent()
    {
        var (tools, _) = CreateTools();

        var first = await tools.setup_agent_workflow(project: "demo");
        var second = await tools.setup_agent_workflow(project: "demo");

        Assert.Contains("Created", first);
        Assert.Contains("(nothing — everything already existed)", second);
    }

    [Fact]
    public async Task SetupAgentWorkflow_NeverOverwritesEditedTemplates()
    {
        var (tools, workspace) = CreateTools();
        await tools.setup_agent_workflow();

        var adrTemplate = workspace.GetVaultTemplatePath("adr")!;
        await File.WriteAllTextAsync(adrTemplate, "MY CUSTOM TEMPLATE {{decision}}", Encoding.UTF8);

        await tools.setup_agent_workflow();

        Assert.Equal("MY CUSTOM TEMPLATE {{decision}}", await File.ReadAllTextAsync(adrTemplate));
    }

    [Fact]
    public async Task SetupAgentWorkflow_ConfigPatchIsAppendOnlyAndNotDuplicated()
    {
        var (tools, _) = CreateTools();
        var configPath = Path.Combine(_fixture.VaultPath, ".kioku", "config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(configPath, "# my existing config\nfolders:\n  inbox: \"Inbox\"\n", Encoding.UTF8);

        await tools.setup_agent_workflow();
        await tools.setup_agent_workflow();

        var content = await File.ReadAllTextAsync(configPath);
        Assert.StartsWith("# my existing config", content);
        Assert.Contains("# engineering:", content);
        Assert.Equal(1, CountOccurrences(content, "Agent workflow (engineering tools)"));
    }

    // Templates

    [Fact]
    public async Task RecordAdr_VaultTemplateOverridesEmbeddedDefault()
    {
        var (tools, workspace) = CreateTools();
        var templatesDir = Path.Combine(_fixture.VaultPath, "Templates", "kioku");
        Directory.CreateDirectory(templatesDir);
        await File.WriteAllTextAsync(
            Path.Combine(templatesDir, "adr.md"), "CUSTOM BODY: {{decision}}", Encoding.UTF8);

        await tools.record_adr("demo", "Use custom", "ctx", "the decision", "cons");

        var files = Directory.GetFiles(workspace.GetSubfolder("demo", "decisions"), "ADR-*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("CUSTOM BODY: the decision", content);
        Assert.DoesNotContain("## Consequences", content);
    }

    [Fact]
    public void EmbeddedTemplates_ExistForAllTemplateKeys()
    {
        foreach (var key in ProjectWorkspaceService.TemplateKeys)
        {
            var content = ProjectWorkspaceService.ReadEmbeddedTemplate(key);
            Assert.False(string.IsNullOrWhiteSpace(content), $"embedded template '{key}' is empty");
        }
    }

    // Obsidian-native frontmatter properties (aliases, cssclasses)

    [Fact]
    public async Task RecordAdr_GetsAliasAndCssClass()
    {
        var (tools, workspace) = CreateTools();

        await tools.record_adr("demo", "Use SQLite", "ctx", "d", "c");

        var file = Directory.GetFiles(workspace.GetSubfolder("demo", "decisions"), "ADR-*.md").Single();
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("aliases:", content);
        Assert.Contains("  - ADR-0001", content);
        Assert.Contains("cssclasses:", content);
        Assert.Contains("  - kioku-adr", content);
    }

    [Theory]
    [InlineData("bugs", "kioku-bug")]
    [InlineData("plans", "kioku-plan")]
    [InlineData("backlog", "kioku-idea")]
    public async Task OtherDocTypes_GetCssClassButNoAlias(string subfolderKey, string expectedCssClass)
    {
        var (tools, workspace) = CreateTools();

        _ = subfolderKey switch
        {
            "bugs" => await tools.log_bug("demo", "Crash", "s", "rc", "f"),
            "plans" => await tools.create_plan("demo", "Search feature", "obj", "- [ ] step"),
            "backlog" => await tools.add_backlog_item("demo", "Faster index", "desc"),
            _ => throw new InvalidOperationException(),
        };

        var file = Directory.GetFiles(workspace.GetSubfolder("demo", subfolderKey)).Single();
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains($"  - {expectedCssClass}", content);
        Assert.DoesNotContain("aliases:", content);
    }

    [Fact]
    public async Task AddKnowledge_BothBranches_GetKnowledgeCssClass()
    {
        var (tools, workspace) = CreateTools();

        await tools.add_knowledge("General note", "content", project: "demo");
        await tools.add_knowledge("Standalone note", "content");

        var projectContent = await File.ReadAllTextAsync(
            Path.Combine(workspace.GetSubfolder("demo", "knowledge"), "General-note.md"));
        var generalContent = await File.ReadAllTextAsync(
            Path.Combine(workspace.KnowledgeRoot, "Standalone-note.md"));

        Assert.Contains("  - kioku-knowledge", projectContent);
        Assert.Contains("  - kioku-knowledge", generalContent);
    }

    [Fact]
    public async Task ProjectMoc_GetsCssClass()
    {
        var (tools, workspace) = CreateTools();

        await tools.record_adr("demo", "Use SQLite", "ctx", "d", "c");

        var moc = await File.ReadAllTextAsync(Path.Combine(workspace.GetProjectFolder("demo"), "demo.md"));
        Assert.Contains("  - kioku-project-moc", moc);
    }

    [Fact]
    public async Task ProjectSession_GetsCssClass()
    {
        var sessions = CreateSessionTools();

        await sessions.start_work_session(project: "demo", agent: "claude");

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, CreateBridge(config));
        var file = Directory.GetFiles(workspace.GetSubfolder("demo", "sessions")).Single();
        var content = await File.ReadAllTextAsync(file);

        Assert.Contains("  - kioku-session", content);
    }

    // Engineering template management tools

    [Fact]
    public async Task ListEngineeringTemplates_ShowsEmbeddedByDefault()
    {
        var (tools, _) = CreateTools();

        var result = await tools.list_engineering_templates();

        Assert.Contains("**adr**", result);
        Assert.Contains("using embedded default", result);
        Assert.Contains("{{decision}}", result);
    }

    [Fact]
    public async Task GetEngineeringTemplate_UnknownType_ReturnsError()
    {
        var (tools, _) = CreateTools();

        var result = await tools.get_engineering_template("banana");

        Assert.StartsWith("[error]", result);
        Assert.Contains("adr", result);
    }

    [Fact]
    public async Task GetEngineeringTemplate_ReturnsEmbeddedContentAndVariables()
    {
        var (tools, _) = CreateTools();

        var result = await tools.get_engineering_template("adr");

        Assert.Contains("embedded default", result);
        Assert.Contains("{{decision}}", result);
        Assert.Contains("ADR-{{number}}", result);
    }

    [Fact]
    public async Task SetEngineeringTemplate_CreatesOverride_ThenGetReflectsIt()
    {
        var (tools, workspace) = CreateTools();

        var setResult = await tools.set_engineering_template("adr", "CUSTOM: {{decision}}");
        Assert.Contains("[ok]", setResult);

        var overridePath = workspace.GetVaultTemplatePath("adr");
        Assert.True(File.Exists(overridePath));

        var getResult = await tools.get_engineering_template("adr");
        Assert.Contains("override:", getResult);
        Assert.Contains("CUSTOM: {{decision}}", getResult);
    }

    [Fact]
    public async Task SetEngineeringTemplate_OverwritesExistingOverride()
    {
        var (tools, _) = CreateTools();
        await tools.set_engineering_template("adr", "FIRST VERSION");

        var result = await tools.set_engineering_template("adr", "SECOND VERSION");

        Assert.Contains("[ok]", result);
        var getResult = await tools.get_engineering_template("adr");
        Assert.Contains("SECOND VERSION", getResult);
        Assert.DoesNotContain("FIRST VERSION", getResult);
    }

    [Fact]
    public async Task SetEngineeringTemplate_UnrecognizedVariable_ReturnsWarningButStillSaves()
    {
        var (tools, _) = CreateTools();

        var result = await tools.set_engineering_template("adr", "{{not_a_real_var}}");

        Assert.Contains("[ok]", result);
        Assert.Contains("[warning]", result);
        Assert.Contains("not_a_real_var", result);
    }

    [Fact]
    public async Task SetEngineeringTemplate_ResetToDefault_RemovesOverride()
    {
        var (tools, workspace) = CreateTools();
        await tools.set_engineering_template("adr", "CUSTOM: {{decision}}");

        var result = await tools.set_engineering_template("adr", reset_to_default: true);

        Assert.Contains("[ok]", result);
        Assert.False(File.Exists(workspace.GetVaultTemplatePath("adr")));

        var getResult = await tools.get_engineering_template("adr");
        Assert.Contains("embedded default", getResult);
    }

    [Fact]
    public async Task SetEngineeringTemplate_ResetWhenNoOverrideExists_IsNoOp()
    {
        var (tools, _) = CreateTools();

        var result = await tools.set_engineering_template("adr", reset_to_default: true);

        Assert.Contains("[ok]", result);
        Assert.Contains("already uses the embedded default", result);
    }

    [Fact]
    public async Task SetEngineeringTemplate_UnknownType_ReturnsError()
    {
        var (tools, _) = CreateTools();

        var result = await tools.set_engineering_template("banana", "content");

        Assert.StartsWith("[error]", result);
    }

    [Fact]
    public async Task SetEngineeringTemplate_NeverTriggersTemplaterEvaluation()
    {
        var (tools, workspace) = CreateTools();

        await tools.set_engineering_template("adr", "Templater syntax: <% tp.date.now() %>");

        // No bridge round trip is made when WRITING a template — the literal <% %> must survive.
        var content = await File.ReadAllTextAsync(workspace.GetVaultTemplatePath("adr")!);
        Assert.Contains("<% tp.date.now() %>", content);
    }

    // get_project_context

    [Fact]
    public async Task GetProjectContext_ReflectsManualEditsOnDisk()
    {
        var (tools, workspace) = CreateTools();
        await tools.record_adr("demo", "Use SQLite", "ctx", "original decision", "cons");

        var adrPath = Directory.GetFiles(workspace.GetSubfolder("demo", "decisions"), "ADR-*.md").Single();
        var content = await File.ReadAllTextAsync(adrPath);
        await File.WriteAllTextAsync(adrPath, content.Replace("original decision", "HUMAN EDITED DECISION"), Encoding.UTF8);

        var context = await tools.get_project_context("demo", include_content: true);

        Assert.Contains("HUMAN EDITED DECISION", context);
    }

    [Fact]
    public async Task GetProjectContext_ListsDocsPerTypeWithStatus()
    {
        var (tools, _) = CreateTools();
        await tools.record_adr("demo", "Use SQLite", "ctx", "d", "c");
        await tools.log_bug("demo", "Crash", "s", "rc", "f");
        await tools.create_plan("demo", "Search feature", "obj", "- [ ] step");
        await tools.add_backlog_item("demo", "Faster index", "desc");

        var context = await tools.get_project_context("demo");

        Assert.Contains("Decisions (ADRs) (1)", context);
        Assert.Contains("[accepted] ADR-0001-Use-SQLite", context);
        Assert.Contains("[fixed] BUG-", context);
        Assert.Contains("[draft] PLAN-", context);
        Assert.Contains("[proposed] Faster-index", context);
    }

    [Fact]
    public async Task GetProjectContext_TypeFilterAndUnknownProject()
    {
        var (tools, _) = CreateTools();
        await tools.record_adr("demo", "Use SQLite", "ctx", "d", "c");
        await tools.log_bug("demo", "Crash", "s", "rc", "f");

        var filtered = await tools.get_project_context("demo", types: "adr");
        Assert.Contains("Decisions (ADRs)", filtered);
        Assert.DoesNotContain("## Bugs", filtered);

        Assert.StartsWith("[error]", await tools.get_project_context("nope"));
        Assert.StartsWith("[error]", await tools.get_project_context("demo", types: "banana"));
    }

    // add_knowledge

    [Fact]
    public async Task AddKnowledge_GeneralGoesToKnowledgeRootWithoutProjectField()
    {
        var (tools, workspace) = CreateTools();

        var result = await tools.add_knowledge("Local setup", "Run docker compose up.");

        Assert.Contains("Knowledge/Local-setup.md", result);
        var content = await File.ReadAllTextAsync(Path.Combine(workspace.KnowledgeRoot, "Local-setup.md"));
        Assert.Contains("type: knowledge", content);
        Assert.DoesNotContain("project:", content);
        Assert.Contains("Run docker compose up.", content);
    }

    [Fact]
    public async Task AddKnowledge_ProjectScopedGoesToProjectSubfolder()
    {
        var (tools, workspace) = CreateTools();

        await tools.add_knowledge("Deploy notes", "Use the staging cluster.", project: "demo");

        var path = Path.Combine(workspace.GetSubfolder("demo", "knowledge"), "Deploy-notes.md");
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("project: demo", content);
    }

    // list_projects

    [Fact]
    public async Task ListProjects_ShowsCountsAndHandlesEmptyRoot()
    {
        var (tools, _) = CreateTools();

        Assert.StartsWith("[info]", await tools.list_projects());

        await tools.record_adr("demo", "Use SQLite", "ctx", "d", "c");
        var result = await tools.list_projects();

        Assert.Contains("**demo**", result);
        Assert.Contains("decisions: 1", result);
    }

    // Duplicate protection

    [Fact]
    public async Task CreateDoc_ErrorsWhenNoteAlreadyExists()
    {
        var (tools, _) = CreateTools();
        await tools.add_backlog_item("demo", "Same idea", "desc");

        var second = await tools.add_backlog_item("demo", "Same idea", "other desc");

        Assert.StartsWith("[error]", second);
        Assert.Contains("already exists", second);
    }

    // Project sessions

    [Fact]
    public async Task StartWorkSession_WithProject_CreatesAgentNamedNote()
    {
        var sessions = CreateSessionTools();

        var result = await sessions.start_work_session(
            goal: "Implement search", project: "demo", agent: "claude");

        Assert.StartsWith("[ok]", result);
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, CreateBridge(config));
        var file = Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "sessions")));

        Assert.Matches(@"\d{4}-\d{2}-\d{2}-\d{4}-claude\.md$", file);
        var content = await File.ReadAllTextAsync(file);
        Assert.Contains("agent: claude", content);
        Assert.Contains("project: demo", content);
        Assert.Contains("status: active", content);
        Assert.Contains("## Summary", content);
        Assert.Contains("Implement search", content);
    }

    [Fact]
    public async Task EndWorkSession_WithProject_WritesSummaryAtTop()
    {
        var sessions = CreateSessionTools();
        await sessions.start_work_session(project: "demo", agent: "claude");

        var result = await sessions.end_work_session(
            summary: "Implemented the search feature end to end.", project: "demo");

        Assert.StartsWith("[ok]", result);
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, CreateBridge(config));
        var file = Assert.Single(Directory.GetFiles(workspace.GetSubfolder("demo", "sessions")));
        var content = await File.ReadAllTextAsync(file);

        Assert.Contains("status: done", content);
        var summaryIndex = content.IndexOf("Implemented the search feature end to end.", StringComparison.Ordinal);
        var logIndex = content.IndexOf("## Log", StringComparison.Ordinal);
        Assert.True(summaryIndex >= 0, "summary not written");
        Assert.True(summaryIndex < logIndex, "summary should appear before the ## Log section");
    }

    [Fact]
    public async Task GetProjectContext_IncludesSessionSummary()
    {
        var (tools, _) = CreateTools();
        var sessions = CreateSessionTools();
        await sessions.start_work_session(project: "demo", agent: "codex");
        await sessions.end_work_session(summary: "Refactored the parser.", project: "demo");

        var context = await tools.get_project_context("demo");

        Assert.Contains("Recent sessions", context);
        Assert.Contains("Refactored the parser.", context);
    }

    // Helper units

    [Theory]
    [InlineData("claude-code 2.1", "claude")]
    [InlineData("Codex CLI", "codex")]
    [InlineData("My Custom Agent", "my-custom-agent")]
    [InlineData("", "agent")]
    [InlineData(null, "agent")]
    public void NormalizeAgentName_MapsClientInfoToSlug(string? raw, string expected)
    {
        Assert.Equal(expected, SessionContextTools.NormalizeAgentName(raw));
    }

    [Fact]
    public void WriteSummarySection_ReplacesPlaceholderAndPreservesOtherSections()
    {
        var content = "---\nstatus: done\n---\n# T\n\n## Summary\n\n_(placeholder)_\n\n## Log\n\nwork\n";

        var updated = SessionContextTools.WriteSummarySection(content, "The real summary.");

        Assert.Contains("## Summary\n\nThe real summary.\n\n## Log", updated);
        Assert.DoesNotContain("_(placeholder)_", updated);
        Assert.Contains("work", updated);
    }

    [Fact]
    public void WriteSummarySection_NoSummaryHeading_ReturnsContentUnchanged()
    {
        var content = "---\nstatus: done\n---\n# Legacy session\n\n## Notes\n";

        Assert.Equal(content, SessionContextTools.WriteSummarySection(content, "summary"));
    }

    [Fact]
    public void ExtractSection_ReturnsContentUpToNextHeading()
    {
        var content = "# T\n\n## Summary\n\nline one\nline two\n\n## Log\n\nother\n";

        var section = EngineeringWorkflowTools.ExtractSection(content, "## Summary");

        Assert.Contains("line one", section);
        Assert.Contains("line two", section);
        Assert.DoesNotContain("other", section);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
