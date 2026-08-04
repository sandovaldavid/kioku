using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kioku.Mcp.Server.Services;

public sealed class VaultConfigService
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

    private readonly VaultConfigData _data;
    private readonly string _vaultPath;

    public VaultConfigService(KiokuConfiguration config, ILogger<VaultConfigService> logger)
    {
        _vaultPath = config.VaultPath;
        var configPath = Path.Combine(config.VaultPath, ".kioku", "config.yml");

        if (File.Exists(configPath))
        {
            try
            {
                var yaml = File.ReadAllText(configPath);
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                _data = deserializer.Deserialize<VaultConfigData>(yaml) ?? new VaultConfigData();
            }
            catch (Exception ex)
            {
                logger.Warn(
                    ex,
                    "Malformed vault config at '{ConfigPath}'. Using empty defaults. Fix the YAML and restart.",
                    configPath);
                _data = new VaultConfigData();
            }
        }
        else
        {
            _data = new VaultConfigData();
        }
    }

    public string? GetFolder(string key) =>
        _data.Folders?.TryGetValue(key, out var f) == true ? f : null;

    public string? GetDomainForFolder(string folderPath)
    {
        if (_data.Domains is null)
        {
            return null;
        }

        // Try exact match first, then prefix match (longest prefix wins)
        if (_data.Domains.TryGetValue(folderPath, out var exact))
        {
            return exact;
        }

        var best = _data.Domains
            .Where(kv => folderPath.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        return best;
    }

    public NoteDefaults? GetDefaults(string typeKey) =>
        _data.Defaults?.TryGetValue(typeKey, out var d) == true ? d : null;

    public HashSet<string> ExcludeFolders =>
        _data.Exclude is not null ? [.. _data.Exclude] : [];

    public string VaultName => _data.Vault?.Name ?? string.Empty;

    /// <summary>Whether Kioku should maintain an <c>updated</c>/<c>modified</c> field on writes.</summary>
    public bool MaintainUpdated => _data.Frontmatter?.MaintainUpdated == true;

    /// <summary>Whether generated MOCs and folder notes may refresh after mutations.</summary>
    public bool RefreshGeneratedIndexes =>
        string.Equals(_data.GeneratedIndexes?.Refresh, "on_mutation", StringComparison.OrdinalIgnoreCase);

    public string? ConfiguredInbox => GetFolder("inbox");

    public IReadOnlyList<string> EnabledCapabilityGroups => KnownGroups
        .Where(IsGroupEnabled)
        .ToArray();

    public IReadOnlyList<string> DisabledCapabilityGroups => KnownGroups
        .Where(group => !IsGroupEnabled(group) && !RemovedGroups.Contains(group))
        .ToArray();

    public IReadOnlyList<string> RemovedCapabilityGroups => RemovedGroups.OrderBy(x => x).ToArray();

    public static IReadOnlyList<string> KnownGroups { get; } =
    [
        "tasks", "organization", "sessions", "workflows", "graph", "research", "generation",
        "css", "assets", "bridge", "plugin", "engineering", "coordination"
    ];

    /// <summary>
    /// Returns inherited tags for a folder path via longest-prefix match.
    /// Empty list if no match or auto_tags.inherit not configured.
    /// </summary>
    public IReadOnlyList<string> GetInheritedTags(string folderPath)
    {
        if (_data.AutoTags?.Inherit is null)
        {
            return [];
        }

        var best = _data.AutoTags.Inherit
            .Where(kv => folderPath.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();

        return best ?? [];
    }

    /// <summary>Fields that should never be duplicated as tags. Default: ["domain","type","status"].</summary>
    public IReadOnlyList<string> ExcludeFromTags =>
        _data.AutoTags?.ExcludeFromTags is { Count: > 0 } list ? list : ["domain", "type", "status"];

    /// <summary>
    /// Resolves the vault-relative template file path to use when creating a note in
    /// <paramref name="targetFolderRelativePath"/>. Checks the vault's own explicit
    /// <c>template_folders</c> override first (longest-prefix match, same pattern as
    /// <see cref="GetDomainForFolder"/>), then falls back to whatever Templater itself has
    /// configured under Settings → Folder Templates — so a user who already set that up in
    /// Templater gets it respected automatically, with zero Kioku-specific configuration.
    /// Returns null when neither source has a match.
    /// </summary>
    public async Task<string?> ResolveFolderTemplateAsync(string targetFolderRelativePath)
    {
        var configuredOverride = _data.TemplateFolders?
            .Where(kv => targetFolderRelativePath.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .Select(kv => kv.Value)
            .FirstOrDefault();
        if (configuredOverride is not null)
        {
            return configuredOverride;
        }

        var templaterPairs = await TemplaterFolderTemplates.ReadAsync(_vaultPath);
        return templaterPairs
            .Where(p => targetFolderRelativePath.StartsWith(p.Folder, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(p => p.Folder.Length)
            .Select(p => p.Template)
            .FirstOrDefault();
    }

    private static readonly Dictionary<string, string> DefaultEngineeringSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ["decisions"] = "decisions",
        ["bugs"] = "bugs",
        ["plans"] = "plans",
        ["knowledge"] = "knowledge",
        ["sessions"] = "sessions",
        ["daily"] = "daily",
        ["tickets"] = "tickets",
        ["backlog"] = "backlog",
    };

    /// <summary>
    /// Returns the per-project subfolder name for an engineering doc type key
    /// (decisions, bugs, plans, knowledge, sessions, daily, tickets, backlog).
    /// Falls back to the built-in default when not configured.
    /// </summary>
    public string GetEngineeringSubfolder(string key)
    {
        if (_data.Engineering?.Subfolders?.TryGetValue(key, out var configured) == true &&
            !string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return DefaultEngineeringSubfolders.TryGetValue(key, out var fallback) ? fallback : key;
    }

    /// <summary>
    /// Determines whether a tool capability group should be registered.
    /// </summary>
    public bool IsGroupEnabled(string groupName)
    {
        if (RemovedGroups.Contains(groupName))
        {
            return false;
        }

        var caps = _data.Capabilities;
        if (caps is null)
        {
            return !DefaultDisabledGroups.Contains(groupName);
        }

        var disabled = caps.Disabled ?? [];
        if (disabled.Contains("*", StringComparer.OrdinalIgnoreCase) ||
            disabled.Contains(groupName, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var enabled = caps.Enabled ?? [];
        if (caps.RequireExplicit)
        {
            return enabled.Contains(groupName, StringComparer.OrdinalIgnoreCase);
        }

        // A partial capabilities block must not silently re-enable the default-off
        // groups: they stay off unless explicitly listed in 'enabled'.
        if (enabled.Contains(groupName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return !DefaultDisabledGroups.Contains(groupName);
    }
}

public sealed class VaultConfigData
{
    public VaultInfo? Vault { get; init; }
    public Dictionary<string, string>? Folders { get; init; }
    public Dictionary<string, string>? Domains { get; init; }
    public Dictionary<string, NoteDefaults>? Defaults { get; init; }
    public List<string>? Exclude { get; init; }
    public AutoTagsConfig? AutoTags { get; init; }

    /// <summary>Folder prefix -&gt; vault-relative template file path (longest prefix wins).</summary>
    public Dictionary<string, string>? TemplateFolders { get; init; }

    public CapabilitiesConfig? Capabilities { get; init; }
    public EngineeringConfig? Engineering { get; init; }
    public FrontmatterConfig? Frontmatter { get; init; }
    public GeneratedIndexesConfig? GeneratedIndexes { get; init; }
}

public sealed class FrontmatterConfig
{
    public bool MaintainUpdated { get; init; }
}

public sealed class GeneratedIndexesConfig
{
    public string? Refresh { get; init; }
}

public sealed class EngineeringConfig
{
    /// <summary>Per-project subfolder names keyed by doc type (decisions, bugs, plans, ...).</summary>
    public Dictionary<string, string>? Subfolders { get; init; }
}

public sealed class CapabilitiesConfig
{
    /// <summary>
    /// Tool groups that should be disabled. Use '*' to disable all optional groups.
    /// Known groups: css, assets, research, graph, workflows, organization, sessions, bridge,
    /// plugin, tasks, generation, engineering, coordination.
    /// </summary>
    public List<string>? Disabled { get; init; }

    /// <summary>
    /// When true, only explicitly enabled groups are registered. Requires Enabled to be set.
    /// </summary>
    public bool RequireExplicit { get; init; }

    /// <summary>
    /// Tool groups that should be enabled when RequireExplicit is true.
    /// </summary>
    public List<string>? Enabled { get; init; }
}

public sealed class VaultInfo
{
    public string? Name { get; init; }
}

public sealed class NoteDefaults
{
    public string? Type { get; init; }
    public string? Status { get; init; }
    public string? Domain { get; init; }
    public List<string>? Tags { get; init; }
}

public sealed class AutoTagsConfig
{
    /// <summary>Tags inherited by folder prefix (longest prefix wins).</summary>
    public Dictionary<string, List<string>>? Inherit { get; init; }

    /// <summary>Frontmatter fields to never add as tags (e.g. domain, type, status).</summary>
    public List<string>? ExcludeFromTags { get; init; }
}
