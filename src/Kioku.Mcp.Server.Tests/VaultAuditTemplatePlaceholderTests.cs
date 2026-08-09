using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultAuditTemplatePlaceholderTests
{
    [Fact]
    public async Task AuditVault_Classifies_empty_links_in_configured_template_folder_separately()
    {
        await using var harness = await AuditHarness.CreateAsync(
            (".kioku/config.yml", "folders:\n  templates: Templates\n"),
            ("Templates/template.md", "- [[]]\n- ![[]]"));

        var result = await harness.Tools.audit_vault();
        var data = Data(result);
        var counts = data.GetProperty("counts");
        var placeholders = data.GetProperty("links").GetProperty("template_placeholders");

        Assert.Equal(0, counts.GetProperty("malformed_occurrences").GetInt32());
        Assert.Equal(2, counts.GetProperty("template_placeholder_occurrences").GetInt32());
        Assert.Equal(1, counts.GetProperty("unique_template_placeholder_edges").GetInt32());
        Assert.Equal(1, counts.GetProperty("unique_template_placeholder_targets").GetInt32());
        Assert.Equal(2, placeholders.GetProperty("total_occurrences").GetInt32());
        Assert.All(
            placeholders.GetProperty("findings").EnumerateArray(),
            finding =>
            {
                Assert.Equal("template_placeholder", finding.GetProperty("status").GetString());
                Assert.Equal("empty_target_in_template", finding.GetProperty("reason").GetString());
            });
        Assert.Contains("## Template placeholders (2)", Text(result));
        Assert.Contains("2 template placeholders skipped from malformed", Text(result));
    }

    [Fact]
    public async Task AuditVault_Uses_templater_templates_root_without_folder_templates_feature()
    {
        await using var harness = await AuditHarness.CreateAsync(
            (".obsidian/plugins/templater-obsidian/data.json",
                "{\"templates_folder\":\"Scaffolds\",\"enable_folder_templates\":false,\"folder_templates\":[]}"),
            ("Scaffolds/template.md", "[[]]"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("template_placeholder_occurrences").GetInt32());
        Assert.Equal(0, counts.GetProperty("malformed_occurrences").GetInt32());
    }

    [Fact]
    public async Task AuditVault_Uses_explicit_template_file_without_template_root()
    {
        await using var harness = await AuditHarness.CreateAsync(
            (".kioku/config.yml", "template_folders:\n  Projects: Scaffolds/project.md\n"),
            ("Scaffolds/project.md", "[[]]"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("template_placeholder_occurrences").GetInt32());
        Assert.Equal(0, counts.GetProperty("malformed_occurrences").GetInt32());
    }

    [Fact]
    public async Task AuditVault_Empty_link_outside_known_template_remains_malformed()
    {
        await using var harness = await AuditHarness.CreateAsync(("Live.md", "[[]]"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(0, counts.GetProperty("template_placeholder_occurrences").GetInt32());
        Assert.Equal(1, counts.GetProperty("malformed_occurrences").GetInt32());
    }

    [Fact]
    public async Task AuditVault_Unclosed_link_inside_template_remains_malformed()
    {
        await using var harness = await AuditHarness.CreateAsync(
            (".kioku/config.yml", "folders:\n  templates: Templates\n"),
            ("Templates/template.md", "- [[]]\n- [[unclosed"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("template_placeholder_occurrences").GetInt32());
        Assert.Equal(1, counts.GetProperty("malformed_occurrences").GetInt32());
    }

    [Fact]
    public async Task AuditVault_Vault_boundary_traversal_remains_malformed()
    {
        await using var harness = await AuditHarness.CreateAsync(
            ("Folder/Live.md", "[[../../Outside]]"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(1, counts.GetProperty("malformed_occurrences").GetInt32());
        Assert.Equal(0, counts.GetProperty("template_placeholder_occurrences").GetInt32());
    }

    [Fact]
    public async Task AuditVault_Trailing_hash_without_literal_note_remains_malformed()
    {
        await using var harness = await AuditHarness.CreateAsync(("Source.md", "[[C#]]"));

        var result = await harness.Tools.audit_vault();
        var malformed = Data(result).GetProperty("links").GetProperty("malformed");
        var finding = malformed.GetProperty("findings")[0];

        Assert.Equal(1, malformed.GetProperty("total_occurrences").GetInt32());
        Assert.Equal("C#", finding.GetProperty("target").GetString());
        Assert.Equal("C", finding.GetProperty("target_identity").GetString());
        Assert.Equal("malformed", finding.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AuditVault_Literal_hash_filename_resolves_before_fragment_fallback()
    {
        await using var harness = await AuditHarness.CreateAsync(
            ("Source.md", "[[C#]]"),
            ("C#.md", "# C sharp"));

        var result = await harness.Tools.audit_vault();
        var counts = Data(result).GetProperty("counts");

        Assert.Equal(0, counts.GetProperty("broken_occurrences").GetInt32());
        Assert.Equal(0, counts.GetProperty("malformed_occurrences").GetInt32());
        Assert.Equal(0, counts.GetProperty("template_placeholder_occurrences").GetInt32());
    }

    [Fact]
    public async Task Template_placeholder_classification_does_not_add_empty_outgoing_links()
    {
        await using var harness = await AuditHarness.CreateAsync(
            (".kioku/config.yml", "folders:\n  templates: Templates\n"),
            ("Templates/template.md", "[[]] [[Real]]"),
            ("Real.md", "# Real"));

        var template = harness.Index.GetNote("Templates/template.md");

        Assert.NotNull(template);
        Assert.Single(template.OutgoingLinks);
        Assert.Equal("Real", template.OutgoingLinks[0]);
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private static JsonElement Data(CallToolResult result) =>
        result.StructuredContent!.Value.GetProperty("data");

    private sealed class AuditHarness(string path, VaultIndexService index, VaultOrganizationTools tools)
        : IAsyncDisposable
    {
        public VaultIndexService Index { get; } = index;
        public VaultOrganizationTools Tools { get; } = tools;

        public static async Task<AuditHarness> CreateAsync(params (string Path, string Content)[] files)
        {
            var vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-audit-template-{Guid.NewGuid():N}");
            Directory.CreateDirectory(vaultPath);

            foreach (var (relativePath, content) in files)
            {
                var fullPath = Path.Combine(vaultPath, relativePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);
            }

            var config = new KiokuConfiguration
            {
                VaultPath = vaultPath,
                MaxSearchResults = 50,
            };
            var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
            var index = new VaultIndexService(
                NullLogger<VaultIndexService>.Instance,
                config,
                vaultConfig: vaultConfig);
            await index.RebuildIndexAsync();

            var embedding = new EmbeddingService(
                config,
                NullLogger<EmbeddingService>.Instance,
                new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                    Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
            var hybrid = new HybridSearchService(index, embedding);
            var tools = new VaultOrganizationTools(index, config, hybrid, embedding, vaultConfig);
            return new AuditHarness(vaultPath, index, tools);
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
