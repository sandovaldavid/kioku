using System.Text;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

public static class NoteHelpers
{
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

        return Path.Combine(vaultPath, normalized);
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
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name
            .Replace(' ', '-')
            .Where(c => !invalid.Contains(c)))
            .Trim('-');
    }
}
