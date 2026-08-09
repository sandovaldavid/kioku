using System.Text;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class ProjectOptionalFolderPathSafetyTests : IAsyncLifetime
{
    private VaultFixture _fixture = null!;

    public async Task InitializeAsync()
    {
        _fixture = new VaultFixture();
        await _fixture.InitializeAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task OptionalFolderConfiguredOutsideVault_IsRejectedBeforeCreation()
    {
        var configPath = Path.Combine(_fixture.VaultPath, ".kioku", "config.yml");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        await File.WriteAllTextAsync(
            configPath,
            """
            engineering:
              subfolders:
                daily: ../../../../outside-daily
            """,
            Encoding.UTF8);

        var config = new KiokuConfiguration { VaultPath = _fixture.VaultPath };
        var vaultConfig = new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
        var bridge = new ObsidianBridgeService(NullLogger<ObsidianBridgeService>.Instance, config);
        var workspace = new ProjectWorkspaceService(config, vaultConfig, bridge);
        var escapedPath = Path.GetFullPath(
            Path.Combine(workspace.GetProjectFolder("demo"), "../../../../outside-daily"));

        var exception = Assert.Throws<InvalidOperationException>(() => workspace.GetSubfolder("demo", "daily"));

        Assert.Contains("escapes the vault security boundary", exception.Message);
        Assert.False(Directory.Exists(escapedPath));
    }
}
