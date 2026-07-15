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
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workflow = new WorkflowTools(_fixture.Index, config, vaultConfig, bridge);
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

    [Fact]
    public async Task ManageTemplates_VaultSetListAndGet_PreservesTemplateBehavior()
    {
        var (workflow, _) = CreateTools();

        var setResult = await workflow.manage_templates(
            scope: "vault", action: "set", name: "daily", content: "# {{date}}\n\n{{title}}");

        Assert.StartsWith("[ok] Template created: Templates/daily.md", setResult);
        Assert.Contains("{{date}}", setResult);

        var listResult = await workflow.manage_templates(scope: "vault", action: "list");
        Assert.Contains("**daily**", listResult);
        Assert.Contains("{{date}}", listResult);

        var getResult = await workflow.manage_templates(scope: "vault", action: "get", name: "daily");
        Assert.Contains("# {{date}}", getResult);
        Assert.Contains("{{title}}", getResult);
    }

    [Fact]
    public async Task ManageTemplates_VaultSetDoesNotOverwriteExistingTemplate()
    {
        var (workflow, _) = CreateTools();
        await workflow.manage_templates(scope: "vault", action: "set", name: "daily", content: "FIRST");

        var result = await workflow.manage_templates(scope: "vault", action: "set", name: "daily", content: "SECOND");

        Assert.StartsWith("[error]", result);
        Assert.Contains("already exists", result);
    }

    [Fact]
    public async Task ManageTemplates_EngineeringSetGetListAndReset_UsesVaultOverride()
    {
        var (workflow, _) = CreateTools();

        var setResult = await workflow.manage_templates(
            scope: "engineering", action: "set", type_key: "adr", content: "CUSTOM: {{decision}}");
        Assert.StartsWith("[ok]", setResult);

        var getResult = await workflow.manage_templates(scope: "engineering", action: "get", type_key: "adr");
        Assert.Contains("override:", getResult);
        Assert.Contains("CUSTOM: {{decision}}", getResult);

        var listResult = await workflow.manage_templates(scope: "engineering", action: "list");
        Assert.Contains("**adr**", listResult);
        Assert.Contains("using embedded default", listResult);
        Assert.Contains("override at Templates/kioku/adr.md", listResult);

        var resetResult = await workflow.manage_templates(
            scope: "engineering", action: "set", type_key: "adr", reset_to_default: true);
        Assert.StartsWith("[ok]", resetResult);
        Assert.Contains("embedded default", resetResult);
    }

    [Theory]
    [InlineData("other", "list")]
    [InlineData("vault", "delete")]
    public async Task ManageTemplates_InvalidScopeOrAction_ReturnsError(string scope, string action)
    {
        var (workflow, _) = CreateTools();

        var result = await workflow.manage_templates(scope, action);

        Assert.StartsWith("[error]", result);
    }

    [Fact]
    public async Task ManageTemplates_EngineeringSetUnknownVariable_ReturnsWarning()
    {
        var (workflow, _) = CreateTools();

        var result = await workflow.manage_templates(
            scope: "engineering", action: "set", type_key: "adr", content: "{{not_a_real_var}}");

        Assert.Contains("[ok]", result);
        Assert.Contains("[warning]", result);
        Assert.Contains("not_a_real_var", result);
    }
}
