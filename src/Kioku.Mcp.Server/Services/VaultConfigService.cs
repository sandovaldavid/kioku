using Microsoft.Extensions.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kioku.Mcp.Server.Services;

public sealed class VaultConfigService
{
    private readonly VaultConfigData _data;

    public VaultConfigService(KiokuConfiguration config, ILogger<VaultConfigService> logger)
    {
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
                logger.LogWarning(
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
    /// Returns the body template for the given note type key (e.g. "zettel", "literature").
    /// Returns null if no template is configured — callers should fall back to their hardcoded body.
    /// </summary>
    public string? GetTemplate(string typeKey) =>
        _data.Templates?.TryGetValue(typeKey, out var t) == true ? t : null;
}

public sealed class VaultConfigData
{
    public VaultInfo? Vault { get; init; }
    public Dictionary<string, string>? Folders { get; init; }
    public Dictionary<string, string>? Domains { get; init; }
    public Dictionary<string, NoteDefaults>? Defaults { get; init; }
    public List<string>? Exclude { get; init; }
    public AutoTagsConfig? AutoTags { get; init; }
    public Dictionary<string, string>? Templates { get; init; }
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
