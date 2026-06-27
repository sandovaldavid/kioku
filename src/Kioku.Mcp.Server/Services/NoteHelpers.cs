using System.Text;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

public static class NoteHelpers
{
    /// <summary>
    /// Ensures that a candidate path remains inside the vault root after canonicalization.
    /// Throws <see cref="InvalidOperationException"/> if the path escapes the vault.
    /// </summary>
    /// <param name="vaultRoot">Absolute path to the vault root directory.</param>
    /// <param name="candidate">Candidate path to validate (may be relative or absolute).</param>
    /// <returns>The canonicalized absolute path, guaranteed to be inside the vault.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the path escapes the vault.</exception>
    public static string EnsureInsideVault(string vaultRoot, string candidate)
    {
        var root = Path.GetFullPath(vaultRoot);
        var full = Path.GetFullPath(candidate);
        var rootWithSep = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!full.StartsWith(rootWithSep, StringComparison.Ordinal) &&
            !full.Equals(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Path escapes the vault: '{candidate}' resolves outside '{vaultRoot}'.");
        }

        return full;
    }

    public static Note? ResolveNote(string nameOrPath, VaultIndexService vault)
    {
        var all = vault.GetAllNotes();

        // Try exact absolute path (fast path)
        var byPath = vault.GetNote(nameOrPath);
        if (byPath is not null)
        {
            return byPath;
        }

        // Try exact name (without extension)
        var byName = all.FirstOrDefault(n =>
            n.Name.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase));
        if (byName is not null)
        {
            return byName;
        }

        // Try vault-relative path (with or without .md extension)
        var normalized = nameOrPath.TrimStart('/').Replace('\\', '/');
        var byRelPath = all.FirstOrDefault(n =>
            n.VaultRelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            n.VaultRelativePath.Equals(normalized + ".md", StringComparison.OrdinalIgnoreCase));
        if (byRelPath is not null)
        {
            return byRelPath;
        }

        // Try file name without extension (for paths like "folder/note")
        var nameOnly = Path.GetFileNameWithoutExtension(nameOrPath);
        return all.FirstOrDefault(n =>
            n.Name.Equals(nameOnly, StringComparison.OrdinalIgnoreCase));
    }

    public static string BuildFilePath(string name, string vaultPath)
    {
        var normalized = name.Replace('/', Path.DirectorySeparatorChar)
                             .Replace('\\', Path.DirectorySeparatorChar);
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".md";
        }

        var combined = Path.Combine(vaultPath, normalized);
        return EnsureInsideVault(vaultPath, combined);
    }

    public static List<string> ParseTags(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return [];
        }

        return [.. tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public static string BuildFrontmatter(
        IEnumerable<string> tags,
        string? type = null,
        string? status = null,
        DateOnly? date = null,
        string? zettelId = null,
        string? domain = null,
        IReadOnlyDictionary<string, string>? extraFields = null)
    {
        var sb = new StringBuilder("---\n");

        var tagList = tags.ToList();
        if (tagList.Count > 0)
        {
            sb.AppendLine("tags:");
            foreach (var tag in tagList)
            {
                sb.AppendLine($"  - {tag}");
            }
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            sb.AppendLine($"type: {type}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            sb.AppendLine($"status: {status}");
        }

        if (!string.IsNullOrWhiteSpace(domain))
        {
            sb.AppendLine($"domain: {domain}");
        }

        if (date.HasValue)
        {
            sb.AppendLine($"date: {date:yyyy-MM-dd}");
        }

        if (!string.IsNullOrWhiteSpace(zettelId))
        {
            sb.AppendLine($"zettel_id: \"{zettelId}\"");
        }

        if (extraFields is not null)
        {
            foreach (var (k, v) in extraFields)
            {
                sb.AppendLine($"{k}: {v}");
            }
        }

        sb.AppendLine("---");
        return sb.ToString();
    }

    public static string SanitizeFileName(string name)
    {
        // Use a cross-platform set of invalid filename characters so vaults
        // remain portable across Windows, macOS, and Linux.
        char[] invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        return string.Concat(name
            .Replace(' ', '-')
            .Where(c => !invalid.Contains(c)))
            .Trim('-');
    }

    /// <summary>
    /// Merges user-provided tags with inherited folder tags, removing duplicates
    /// and filtering out values that appear in excludedFields.
    /// Order: user tags first, then inherited additions.
    /// </summary>
    public static List<string> MergeTagsWithInheritance(
        IEnumerable<string> userTags,
        IEnumerable<string> inheritedTags,
        IEnumerable<string>? excludedFields = null)
    {
        var excluded = new HashSet<string>(excludedFields ?? [], StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var tag in userTags.Concat(inheritedTags))
        {
            if (!string.IsNullOrWhiteSpace(tag) && !excluded.Contains(tag) && seen.Add(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    /// <summary>
    /// Expands {{variable}} placeholders in a template string.
    /// Unknown placeholders are left as-is. Variables are matched case-insensitively.
    /// </summary>
    /// <param name="template">Template string with {{variable}} placeholders.</param>
    /// <param name="variables">Key-value pairs of variable name to value.</param>
    /// <returns>Template with all known placeholders replaced.</returns>
    public static string ExpandTemplateVariables(
        string template,
        IReadOnlyDictionary<string, string> variables)
    {
        foreach (var (key, value) in variables)
        {
            template = template.Replace($"{{{{{key}}}}}", value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
        return template;
    }
}
