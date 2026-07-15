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
    public void IsGroupEnabled_DefaultsToCoreGroupsOnly()
    {
        var service = CreateService("");

        Assert.True(service.IsGroupEnabled("tasks"));
        Assert.True(service.IsGroupEnabled("engineering"));
        Assert.False(service.IsGroupEnabled("css"));
        Assert.False(service.IsGroupEnabled("research"));
        Assert.False(service.IsGroupEnabled("git"));
        Assert.False(service.IsGroupEnabled("restore"));
        Assert.False(service.IsGroupEnabled("zettelkasten"));
        Assert.False(service.IsGroupEnabled("graph-analysis"));
    }

    [Fact]
    public void IsGroupEnabled_DisabledGroup_ReturnsFalse()
    {
        var service = CreateService("capabilities:\n  disabled:\n    - git\n    - css");

        Assert.False(service.IsGroupEnabled("git"));
        Assert.False(service.IsGroupEnabled("css"));
        Assert.True(service.IsGroupEnabled("tasks"));
    }

    [Fact]
    public void IsGroupEnabled_PartialCapabilitiesBlock_KeepsDefaultOffGroupsOff()
    {
        var service = CreateService("capabilities:\n  disabled:\n    - tasks");

        Assert.False(service.IsGroupEnabled("tasks"));
        Assert.False(service.IsGroupEnabled("research"));
        Assert.False(service.IsGroupEnabled("bridge"));
        Assert.True(service.IsGroupEnabled("engineering"));
    }

    [Fact]
    public void IsGroupEnabled_EnabledList_OptsInDefaultOffGroupWithoutRequireExplicit()
    {
        var service = CreateService("capabilities:\n  enabled:\n    - research");

        Assert.True(service.IsGroupEnabled("research"));
        Assert.False(service.IsGroupEnabled("bridge"));
        Assert.True(service.IsGroupEnabled("tasks"));
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
        var service = CreateService("capabilities:\n  require_explicit: true\n  enabled:\n    - tasks");

        Assert.True(service.IsGroupEnabled("tasks"));
        Assert.False(service.IsGroupEnabled("css"));
    }

    [Fact]
    public void MutationOptions_DefaultToCompatibilityMode()
    {
        var service = CreateService("");

        Assert.False(service.MaintainUpdated);
        Assert.False(service.RefreshGeneratedIndexes);
    }

    [Fact]
    public void MutationOptions_ReadExplicitOptIns()
    {
        var service = CreateService(
            "frontmatter:\n  maintain_updated: true\ngenerated_indexes:\n  refresh: on_mutation");

        Assert.True(service.MaintainUpdated);
        Assert.True(service.RefreshGeneratedIndexes);
    }

    [Fact]
    public void GeneratedIndexRefresh_RejectsUnknownModes()
    {
        var service = CreateService("generated_indexes:\n  refresh: always");

        Assert.False(service.RefreshGeneratedIndexes);
    }
}
