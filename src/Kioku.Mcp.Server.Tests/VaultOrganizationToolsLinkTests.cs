using System.Text;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Integration tests for audit_vault resolving links that point at real files sitting in
/// excluded folders. Each test builds its own temp vault (not VaultFixture) since the exclude
/// list must exist in .kioku/config.yml before the index is built.
/// </summary>
public class VaultOrganizationToolsLinkTests : IAsyncLifetime
{
    private string _vaultPath = null!;
    private VaultIndexService _index = null!;
    private VaultConfigService _vaultConfig = null!;
    private HybridSearchService _hybrid = null!;
    private EmbeddingService _embedding = null!;
    private KiokuConfiguration _config = null!;

    public async Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);

        Directory.CreateDirectory(Path.Combine(_vaultPath, ".kioku"));
        await File.WriteAllTextAsync(
            Path.Combine(_vaultPath, ".kioku", "config.yml"),
            "exclude:\n  - Templates\n",
            Encoding.UTF8);

        // A real file inside the excluded folder — not indexed, but exists on disk.
        Directory.CreateDirectory(Path.Combine(_vaultPath, "Templates"));
        await File.WriteAllTextAsync(
            Path.Combine(_vaultPath, "Templates", "Example Template.md"), "# template", Encoding.UTF8);

        // A note linking to that excluded file, plus a genuinely nonexistent one.
        await File.WriteAllTextAsync(
            Path.Combine(_vaultPath, "Linker.md"),
            "See [[Example Template]] and [[Nonexistent Note]] for details.",
            Encoding.UTF8);

        _config = new KiokuConfiguration { VaultPath = _vaultPath };
        _vaultConfig = new VaultConfigService(_config, NullLogger<VaultConfigService>.Instance);
        _index = new VaultIndexService(NullLogger<VaultIndexService>.Instance, _config, vaultConfig: _vaultConfig);
        await _index.RebuildIndexAsync();

        _embedding = new EmbeddingService(_config, NullLogger<EmbeddingService>.Instance, new FakeHttpClientFactory(
            new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable)))));
        _hybrid = new HybridSearchService(_index, _embedding);
    }

    public Task DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_vaultPath))
            {
                Directory.Delete(_vaultPath, recursive: true);
            }
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private VaultOrganizationTools CreateTools() =>
        new(_index, _config, _hybrid, _embedding, _vaultConfig);

    [Fact]
    public async Task AuditVault_TargetInExcludedFolder_NotReportedBroken()
    {
        var tools = CreateTools();

        var result = await tools.audit_vault();

        Assert.DoesNotContain("Example Template", result);
        Assert.Contains("Nonexistent Note", result);
    }
}
