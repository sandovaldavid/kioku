using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Kioku.Mcp.Server.Services;

public sealed class VaultConfigService
{
    private readonly VaultConfigData _data;

    public VaultConfigService(KiokuConfiguration config)
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
            catch
            {
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
}

public sealed class VaultConfigData
{
    public VaultInfo? Vault { get; init; }
    public Dictionary<string, string>? Folders { get; init; }
    public Dictionary<string, string>? Domains { get; init; }
    public Dictionary<string, NoteDefaults>? Defaults { get; init; }
    public List<string>? Exclude { get; init; }
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
