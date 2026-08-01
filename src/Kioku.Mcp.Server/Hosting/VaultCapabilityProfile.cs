using Kioku.Mcp.Server.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kioku.Mcp.Server.Hosting;

/// <summary>
/// Minimal startup-only view of capability groups. Runtime services still own the complete vault
/// configuration; this profile only decides which tool types are registered before the host builds.
/// </summary>
internal sealed class VaultCapabilityProfile
{
    private static readonly HashSet<string> DefaultDisabledGroups =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "research", "generation", "css", "assets", "bridge", "plugin", "coordination",
        };

    private static readonly HashSet<string> RemovedGroups =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "git", "restore", "zettelkasten", "graph-analysis",
        };

    private readonly CapabilitiesConfig? _capabilities;

    private VaultCapabilityProfile(CapabilitiesConfig? capabilities)
    {
        _capabilities = capabilities;
    }

    internal static VaultCapabilityProfile Load(string vaultPath)
    {
        var path = Path.Combine(vaultPath, ".kioku", "config.yml");
        if (!File.Exists(path))
        {
            return new VaultCapabilityProfile(null);
        }

        try
        {
            var yaml = File.ReadAllText(path);
            var data = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build()
                .Deserialize<VaultConfigData>(yaml);
            return new VaultCapabilityProfile(data?.Capabilities);
        }
        catch
        {
            // VaultConfigService emits the actionable warning after the final container is built.
            return new VaultCapabilityProfile(null);
        }
    }

    internal bool IsEnabled(string groupName)
    {
        if (RemovedGroups.Contains(groupName))
        {
            return false;
        }

        if (_capabilities is null)
        {
            return !DefaultDisabledGroups.Contains(groupName);
        }

        var disabled = _capabilities.Disabled ?? [];
        if (disabled.Contains("*", StringComparer.OrdinalIgnoreCase) ||
            disabled.Contains(groupName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var enabled = _capabilities.Enabled ?? [];
        if (_capabilities.RequireExplicit)
        {
            return enabled.Contains(groupName, StringComparer.OrdinalIgnoreCase);
        }

        if (enabled.Contains(groupName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return !DefaultDisabledGroups.Contains(groupName);
    }
}
