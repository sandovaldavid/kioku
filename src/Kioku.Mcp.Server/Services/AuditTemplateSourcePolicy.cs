using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Resolves the vault-relative Markdown sources that are known templates for audit-only
/// classification. This does not change indexing, note lookup, graph resolution, or writes.
/// </summary>
internal sealed class AuditTemplateSourcePolicy(
    IReadOnlySet<string> templateFiles,
    IReadOnlyList<string> templateFolders)
{
    public static async Task<AuditTemplateSourcePolicy> CreateAsync(
        KiokuConfiguration config,
        VaultConfigService vaultConfig)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddFolder(folders, vaultConfig.ConfiguredTemplatesFolder);
        foreach (var template in vaultConfig.ConfiguredTemplateFiles)
        {
            AddFile(files, template);
        }

        foreach (var pair in await TemplaterFolderTemplates.ReadAsync(config.VaultPath))
        {
            AddFile(files, pair.Template);
        }

        AddFolder(folders, await TemplaterFolderTemplates.ReadTemplatesFolderAsync(config.VaultPath));

        return new AuditTemplateSourcePolicy(
            files,
            folders.OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public bool IsTemplate(Note note)
    {
        var sourceFile = NormalizeFile(note.VaultRelativePath);
        if (templateFiles.Contains(sourceFile))
        {
            return true;
        }

        var sourcePath = NormalizePath(note.VaultRelativePath);
        return templateFolders.Any(folder =>
            sourcePath.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
    }

    private static void AddFile(HashSet<string> files, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = NormalizeFile(path);
        if (normalized.Length > 0)
        {
            files.Add(normalized);
        }
    }

    private static void AddFolder(HashSet<string> folders, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = NormalizePath(path).TrimEnd('/');
        if (normalized.Length > 0)
        {
            folders.Add(normalized);
        }
    }

    private static string NormalizeFile(string path)
    {
        var normalized = NormalizePath(path);
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3]
            : normalized;
    }

    private static string NormalizePath(string path) =>
        path.Replace('\\', '/').Trim().Trim('/');
}
