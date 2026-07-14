using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Folder-based template resolution for ZettelkastenTools: create_zettel/create_moc/
/// create_literature_note fall back to their hardcoded body when no template applies to the
/// target folder, but use a user's configured/Templater-discovered template when one does.
/// </summary>
public class ZettelkastenToolsFolderTemplateTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private ZettelkastenTools CreateTools(VaultConfigService? vaultConfig = null)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        vaultConfig ??= new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var embedding = new EmbeddingService(
            config,
            NullLogger<EmbeddingService>.Instance,
            new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        var hybrid = new HybridSearchService(_fixture.Index, embedding);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        return new ZettelkastenTools(_fixture.Index, embedding, hybrid, config, vaultConfig, bridge);
    }

    private VaultConfigService CreateVaultConfigWithTemplateFolder(string folder, string templatePath)
    {
        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var configDir = Path.Combine(_fixture.VaultPath, ".kioku");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(
            Path.Combine(configDir, "config.yml"),
            $"template_folders:\n  \"{folder}\": \"{templatePath}\"\n",
            Encoding.UTF8);
        return new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    [Fact]
    public async Task CreateZettel_NoTemplateConfigured_UsesDefaultBody()
    {
        var tools = CreateTools();

        var result = await tools.create_zettel("My Idea", "Some content here.", folder: "Zettelkasten", link_related: false);

        Assert.StartsWith("[ok]", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Zettelkasten"), "*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("# My Idea", content);
        Assert.Contains("Some content here.", content);
    }

    [Fact]
    public async Task CreateZettel_ConfiguredTemplate_RendersItInsteadOfDefault()
    {
        var templateDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templateDir);
        await File.WriteAllTextAsync(
            Path.Combine(templateDir, "my-zettel.md"),
            "CUSTOM ZETTEL: {{title}} — {{content}}",
            Encoding.UTF8);
        var vaultConfig = CreateVaultConfigWithTemplateFolder("Zettelkasten", "Templates/my-zettel.md");
        var tools = CreateTools(vaultConfig);

        var result = await tools.create_zettel("My Idea", "the body", folder: "Zettelkasten", link_related: false);

        Assert.StartsWith("[ok]", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Zettelkasten"), "*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("CUSTOM ZETTEL: My Idea — the body", content);
        Assert.DoesNotContain("# My Idea", content);
    }

    [Fact]
    public async Task CreateZettel_TemplaterSyntaxInConfiguredTemplate_DegradesGracefullyWithoutBridge()
    {
        var templateDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templateDir);
        await File.WriteAllTextAsync(
            Path.Combine(templateDir, "my-zettel.md"),
            "{{title}}: <% tp.date.now() %>",
            Encoding.UTF8);
        var vaultConfig = CreateVaultConfigWithTemplateFolder("Zettelkasten", "Templates/my-zettel.md");
        var tools = CreateTools(vaultConfig);

        var result = await tools.create_zettel("My Idea", "body", folder: "Zettelkasten", link_related: false);

        Assert.StartsWith("[ok]", result);
        Assert.Contains("[warning] template contains Templater syntax; left unevaluated", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Zettelkasten"), "*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("<% tp.date.now() %>", content);
    }

    [Fact]
    public async Task CreateMoc_ConfiguredTemplate_WrapsNotesListWithoutReplacingIt()
    {
        var templateDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templateDir);
        await File.WriteAllTextAsync(
            Path.Combine(templateDir, "my-moc.md"),
            "# Custom MOC wrapper\n\n{{moc_list}}",
            Encoding.UTF8);
        var vaultConfig = CreateVaultConfigWithTemplateFolder("Projects", "Templates/my-moc.md");
        var tools = CreateTools(vaultConfig);

        var result = await tools.create_moc("Projects");

        Assert.StartsWith("[ok]", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Projects"), "*-MOC.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("# Custom MOC wrapper", content);
        Assert.Contains("[[Project Alpha]]", content);
        Assert.Contains("[[Project Beta]]", content);
    }

    [Fact]
    public async Task CreateLiteratureNote_NoTemplateConfigured_UsesDefaultBody()
    {
        var tools = CreateTools();

        var result = await tools.create_literature_note("A Book", "An Author", "2024", summary: "great read");

        Assert.StartsWith("[ok]", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Literature"), "*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("**Author:** An Author", content);
        Assert.Contains("great read", content);
    }

    [Fact]
    public async Task CreateLiteratureNote_ConfiguredTemplate_RendersItInsteadOfDefault()
    {
        var templateDir = Path.Combine(_fixture.VaultPath, "Templates");
        Directory.CreateDirectory(templateDir);
        await File.WriteAllTextAsync(
            Path.Combine(templateDir, "my-lit.md"),
            "CUSTOM LIT: {{author}} ({{year}}) — {{summary}}",
            Encoding.UTF8);
        var vaultConfig = CreateVaultConfigWithTemplateFolder("Literature", "Templates/my-lit.md");
        var tools = CreateTools(vaultConfig);

        var result = await tools.create_literature_note("A Book", "An Author", "2024", summary: "great read");

        Assert.StartsWith("[ok]", result);
        var files = Directory.GetFiles(Path.Combine(_fixture.VaultPath, "Literature"), "*.md");
        var content = await File.ReadAllTextAsync(Assert.Single(files));
        Assert.Contains("CUSTOM LIT: An Author (2024) — great read", content);
    }
}
