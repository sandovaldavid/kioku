using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultOrganizationToolsAuditPaginationTests
{
    [Fact]
    public async Task AuditVault_Reports_occurrence_edge_and_target_counts()
    {
        await using var harness = await AuditHarness.CreateAsync(
            ("Source A.md", "[[Missing One]] [[Missing One]] [[Missing Two]] [[Duplicate]]"),
            ("Source B.md", "[[Missing One]] [[../Outside]]"),
            ("Folder A/Duplicate.md", "# A"),
            ("Folder B/Duplicate.md", "# B"));

        var result = await harness.Tools.audit_vault();
        var data = Data(result);
        var counts = data.GetProperty("counts");

        Assert.False(result.IsError);
        Assert.Equal(4, counts.GetProperty("broken_occurrences").GetInt32());
        Assert.Equal(3, counts.GetProperty("unique_broken_edges").GetInt32());
        Assert.Equal(2, counts.GetProperty("unique_broken_targets").GetInt32());
        Assert.Equal(1, counts.GetProperty("ambiguous_occurrences").GetInt32());
        Assert.Equal(1, counts.GetProperty("malformed_occurrences").GetInt32());

        var text = Text(result);
        Assert.Contains("## Broken wikilinks (4)", text);
        Assert.Contains("4 broken occurrences (3 unique edges, 2 unique targets)", text);
    }

    [Fact]
    public async Task AuditVault_Paginates_broken_findings_without_deduplicating_occurrences()
    {
        await using var harness = await AuditHarness.CreateAsync(
            ("Source A.md", "[[Missing One]] [[Missing One]] [[Missing Two]]"),
            ("Source B.md", "[[Missing One]]"));

        var first = await harness.Tools.audit_vault(offset: 0, limit: 2);
        var second = await harness.Tools.audit_vault(offset: 2, limit: 2);
        var finalPartial = await harness.Tools.audit_vault(offset: 3, limit: 2);

        var firstPage = Broken(first);
        var secondPage = Broken(second);
        var finalPage = Broken(finalPartial);

        Assert.Equal(4, firstPage.GetProperty("total_occurrences").GetInt32());
        Assert.Equal(3, firstPage.GetProperty("unique_edges").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("unique_targets").GetInt32());
        Assert.Equal(2, firstPage.GetProperty("returned").GetInt32());
        Assert.True(firstPage.GetProperty("has_more").GetBoolean());

        var firstFindings = firstPage.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, firstFindings.Length);
        Assert.All(firstFindings, finding => Assert.Equal("Missing One", finding.GetProperty("target").GetString()));

        var secondFindings = secondPage.GetProperty("findings").EnumerateArray().ToArray();
        Assert.Equal(2, secondFindings.Length);
        Assert.Equal("Missing Two", secondFindings[0].GetProperty("target").GetString());
        Assert.Equal("Missing One", secondFindings[1].GetProperty("target").GetString());
        Assert.False(secondPage.GetProperty("has_more").GetBoolean());

        Assert.Equal(1, finalPage.GetProperty("returned").GetInt32());
        Assert.False(finalPage.GetProperty("has_more").GetBoolean());
    }

    [Fact]
    public async Task AuditVault_Separates_ambiguous_and_malformed_pages()
    {
        await using var harness = await AuditHarness.CreateAsync(
            ("Source.md", "[[Duplicate]] [[../Outside]]"),
            ("Folder A/Duplicate.md", "# A"),
            ("Folder B/Duplicate.md", "# B"));

        var result = await harness.Tools.audit_vault(limit: 1);
        var links = Data(result).GetProperty("links");
        var ambiguous = links.GetProperty("ambiguous");
        var malformed = links.GetProperty("malformed");
        var broken = links.GetProperty("broken");

        Assert.Equal(0, broken.GetProperty("total_occurrences").GetInt32());
        Assert.Equal(1, ambiguous.GetProperty("total_occurrences").GetInt32());
        Assert.Equal(1, malformed.GetProperty("total_occurrences").GetInt32());
        Assert.Equal("ambiguous", ambiguous.GetProperty("findings")[0].GetProperty("status").GetString());
        Assert.Equal("malformed", malformed.GetProperty("findings")[0].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(-1, 50, "'offset' must be 0 or greater.")]
    [InlineData(0, 0, "'limit' must be greater than 0.")]
    public async Task AuditVault_Rejects_invalid_pagination(int offset, int limit, string expectedMessage)
    {
        await using var harness = await AuditHarness.CreateAsync(("Clean.md", "# Clean"));

        var result = await harness.Tools.audit_vault(offset: offset, limit: limit);
        var root = result.StructuredContent!.Value;

        Assert.True(result.IsError);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Equal("INVALID_ARGUMENT", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains(expectedMessage, Text(result));
    }

    [Fact]
    public async Task AuditVault_Empty_link_sets_have_zero_counts_and_no_more_pages()
    {
        await using var harness = await AuditHarness.CreateAsync(("Clean.md", "# Clean"));

        var result = await harness.Tools.audit_vault(offset: 25, limit: 10);
        var links = Data(result).GetProperty("links");

        foreach (var category in new[] { "broken", "ambiguous", "malformed" })
        {
            var page = links.GetProperty(category);
            Assert.Equal(0, page.GetProperty("total_occurrences").GetInt32());
            Assert.Equal(0, page.GetProperty("unique_edges").GetInt32());
            Assert.Equal(0, page.GetProperty("unique_targets").GetInt32());
            Assert.Equal(0, page.GetProperty("returned").GetInt32());
            Assert.False(page.GetProperty("has_more").GetBoolean());
            Assert.Empty(page.GetProperty("findings").EnumerateArray());
        }
    }

    [Fact]
    public async Task AuditVault_Default_page_preserves_legacy_fifty_item_preview()
    {
        var links = string.Join(" ", Enumerable.Range(1, 55).Select(i => $"[[Missing {i:00}]]"));
        await using var harness = await AuditHarness.CreateAsync(2, ("Source.md", links));

        var result = await harness.Tools.audit_vault();
        var broken = Broken(result);

        Assert.Equal(55, broken.GetProperty("total_occurrences").GetInt32());
        Assert.Equal(50, broken.GetProperty("limit").GetInt32());
        Assert.Equal(50, broken.GetProperty("returned").GetInt32());
        Assert.True(broken.GetProperty("has_more").GetBoolean());
        Assert.Contains("... and 5 more", Text(result));
    }

    [Fact]
    public async Task AuditVault_Uses_larger_configured_result_bound_for_explicit_pages()
    {
        var links = string.Join(" ", Enumerable.Range(1, 80).Select(i => $"[[Missing {i:00}]]"));
        await using var harness = await AuditHarness.CreateAsync(75, ("Source.md", links));

        var result = await harness.Tools.audit_vault(limit: 100);
        var broken = Broken(result);

        Assert.Equal(75, broken.GetProperty("limit").GetInt32());
        Assert.Equal(75, broken.GetProperty("returned").GetInt32());
        Assert.True(broken.GetProperty("has_more").GetBoolean());
    }

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Single().Text;

    private static JsonElement Data(CallToolResult result) =>
        result.StructuredContent!.Value.GetProperty("data");

    private static JsonElement Broken(CallToolResult result) =>
        Data(result).GetProperty("links").GetProperty("broken");

    private sealed class AuditHarness(string path, VaultOrganizationTools tools) : IAsyncDisposable
    {
        public VaultOrganizationTools Tools { get; } = tools;

        public static Task<AuditHarness> CreateAsync(params (string Path, string Content)[] files) =>
            CreateAsync(50, files);

        public static async Task<AuditHarness> CreateAsync(
            int maxSearchResults,
            params (string Path, string Content)[] files)
        {
            var vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-audit-{Guid.NewGuid():N}");
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
                MaxSearchResults = maxSearchResults,
            };
            var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
            var index = new VaultIndexService(NullLogger<VaultIndexService>.Instance, config, vaultConfig: vaultConfig);
            await index.RebuildIndexAsync();

            var embedding = new EmbeddingService(
                config,
                NullLogger<EmbeddingService>.Instance,
                new FakeHttpClientFactory(new FakeHttpMessageHandler((_, _) =>
                    Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
            var hybrid = new HybridSearchService(index, embedding);
            var tools = new VaultOrganizationTools(index, config, hybrid, embedding, vaultConfig);
            return new AuditHarness(vaultPath, tools);
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
