using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class VaultConfigServiceTests : IDisposable
{
    private readonly string _tempVault;

    public VaultConfigServiceTests()
    {
        _tempVault = Path.Combine(Path.GetTempPath(), $"kioku-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempVault);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempVault, recursive: true);
        }
        catch
        {
            // ignore cleanup errors
        }
    }

    private VaultConfigService CreateService(string yaml)
    {
        var configDir = Path.Combine(_tempVault, ".kioku");
        Directory.CreateDirectory(configDir);
        File.WriteAllText(Path.Combine(configDir, "config.yml"), yaml);

        var config = new KiokuConfiguration { VaultPath = _tempVault };
        return new VaultConfigService(config, NullLogger<VaultConfigService>.Instance);
    }

    [Fact]
    public void IsGroupEnabled_DefaultsToTrue()
    {
        var service = CreateService("");

        Assert.True(service.IsGroupEnabled("git"));
        Assert.True(service.IsGroupEnabled("css"));
    }

    [Fact]
    public void IsGroupEnabled_DisabledGroup_ReturnsFalse()
    {
        var service = CreateService("capabilities:\n  disabled:\n    - git\n    - css");

        Assert.False(service.IsGroupEnabled("git"));
        Assert.False(service.IsGroupEnabled("css"));
        Assert.True(service.IsGroupEnabled("research"));
    }

    [Fact]
    public void IsGroupEnabled_DisableAll_ReturnsFalseForOptionalGroups()
    {
        var service = CreateService("capabilities:\n  disabled:\n    - \"*\"");

        Assert.False(service.IsGroupEnabled("git"));
        Assert.False(service.IsGroupEnabled("css"));
    }

    [Fact]
    public void IsGroupEnabled_RequireExplicit_OnlyEnabledGroupsAreTrue()
    {
        var service = CreateService("capabilities:\n  require_explicit: true\n  enabled:\n    - git");

        Assert.True(service.IsGroupEnabled("git"));
        Assert.False(service.IsGroupEnabled("css"));
    }
}
