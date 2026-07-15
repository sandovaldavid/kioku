using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

public static class NoteHelpers
{
    private static readonly Regex UpdatedFieldRegex = new(
        @"^(?<key>updated|modified):.*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    /// <summary>
    /// UTF-8 without BOM for every vault write. Obsidian/Node-authored files never carry a
    /// BOM; Encoding.UTF8 writes one by default, which would leak into touched notes.
    /// </summary>
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

        // Try vault-relative path (with or without .md extension)
        var normalized = nameOrPath.TrimStart('/').Replace('\\', '/');
        var byRelPath = all.SingleOrDefault(n =>
            n.VaultRelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            n.VaultRelativePath.Equals(normalized + ".md", StringComparison.OrdinalIgnoreCase));
        if (byRelPath is not null)
        {
            return byRelPath;
        }

        // Try exact filename only when it is unambiguous. Duplicate basenames are valid in a vault.
        var nameOnly = Path.GetFileNameWithoutExtension(nameOrPath);
        if (!normalized.Contains('/'))
        {
            var byName = all.Where(n => n.Name.Equals(nameOnly, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

        // Finally resolve a unique frontmatter alias. Ambiguous aliases are deliberately rejected.
        var byAlias = all
            .Where(n => n.Metadata.Aliases.Any(alias => alias.Equals(nameOrPath, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return byAlias.Count == 1 ? byAlias[0] : null;
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
        IEnumerable<string>? aliases = null,
        IEnumerable<string>? cssClasses = null,
        IReadOnlyDictionary<string, string>? extraFields = null,
        DateOnly? updated = null)
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

        var aliasList = aliases?.ToList() ?? [];
        if (aliasList.Count > 0)
        {
            sb.AppendLine("aliases:");
            foreach (var alias in aliasList)
            {
                sb.AppendLine($"  - {alias}");
            }
        }

        var cssClassList = cssClasses?.ToList() ?? [];
        if (cssClassList.Count > 0)
        {
            sb.AppendLine("cssclasses:");
            foreach (var cssClass in cssClassList)
            {
                sb.AppendLine($"  - {cssClass}");
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

        if (updated.HasValue)
        {
            sb.AppendLine($"updated: {updated:yyyy-MM-dd}");
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

    /// <summary>
    /// Touches an existing note's declared modification date without rebuilding its YAML.
    /// The operation is deliberately opt-in because many vaults let Obsidian Linter own this field.
    /// </summary>
    public static string TouchUpdated(string content, DateOnly date, bool enabled)
    {
        if (!enabled)
        {
            return content;
        }

        var bodyStart = FrontmatterParser.GetBodyStart(content);
        var value = $"updated: {date:yyyy-MM-dd}";
        if (bodyStart == 0)
        {
            return $"---\n{value}\n---\n{content}";
        }

        if (UpdatedFieldRegex.IsMatch(content[..bodyStart]))
        {
            return UpdatedFieldRegex.Replace(
                content,
                match => $"{match.Groups["key"].Value}: {date:yyyy-MM-dd}",
                1);
        }

        var closeIndex = content.LastIndexOf("---", bodyStart - 1, StringComparison.Ordinal);
        if (closeIndex < 0)
        {
            return content;
        }

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var beforeClose = content[..closeIndex];
        if (!beforeClose.EndsWith(newline, StringComparison.Ordinal))
        {
            beforeClose += newline;
        }

        return beforeClose + value + newline + content[closeIndex..];
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
    /// Appends a Markdown wikilink section (e.g. "## Related") to note content, skipping any
    /// target already present in existingOutgoingLinks — shared by link_related_notes and
    /// apply_link_suggestions so neither duplicates the other's insertion logic.
    /// Returns null when every target is already linked (caller should treat this as a no-op
    /// and skip writing the file, which is what makes repeated calls idempotent).
    /// </summary>
    public static string? AppendLinkSection(
        string currentContent,
        IReadOnlySet<string> existingOutgoingLinks,
        string sectionTitle,
        IEnumerable<(string TargetName, string? Annotation)> targets)
    {
        var newTargets = targets
            .Where(t => !existingOutgoingLinks.Contains(t.TargetName))
            .ToList();

        if (newTargets.Count == 0)
        {
            return null;
        }

        var section = new StringBuilder($"\n\n## {sectionTitle}\n\n");
        foreach (var (name, annotation) in newTargets)
        {
            section.Append($"- [[{name}]]");
            if (!string.IsNullOrEmpty(annotation))
            {
                section.Append($" ({annotation})");
            }

            section.AppendLine();
        }

        return currentContent.TrimEnd() + section;
    }

    /// <summary>
    /// Expands {{variable}} placeholders in a template string.
    /// Unknown placeholders are left as-is. Variables are matched case-insensitively.
    /// Built-in variables are supported: {{date}}, {{time}}, {{datetime}}, {{year}}, {{month}},
    /// {{day}}, {{title}}, and {{uid}}. User-provided variables take precedence over built-ins.
    /// </summary>
    /// <param name="template">Template string with {{variable}} placeholders.</param>
    /// <param name="variables">Key-value pairs of variable name to value.</param>
    /// <param name="noteTitle">Optional note title for the {{title}} built-in.</param>
    /// <param name="now">Optional timestamp for date/time built-ins. Defaults to UTC now.</param>
    /// <returns>Template with all known placeholders replaced.</returns>
    public static string ExpandTemplateVariables(
        string template,
        IReadOnlyDictionary<string, string> variables,
        string? noteTitle = null,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var merged = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

        // User-provided values take precedence; only add built-ins that are absent.
        merged.TryAdd("date", timestamp.ToString("yyyy-MM-dd"));
        merged.TryAdd("time", timestamp.ToString("HH:mm:ss"));
        merged.TryAdd("datetime", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
        merged.TryAdd("year", timestamp.Year.ToString());
        merged.TryAdd("month", timestamp.Month.ToString("D2"));
        merged.TryAdd("day", timestamp.Day.ToString("D2"));
        merged.TryAdd("uid", Guid.NewGuid().ToString("N"));

        if (noteTitle is not null)
        {
            merged.TryAdd("title", noteTitle);
        }

        foreach (var (key, value) in merged)
        {
            template = template.Replace($"{{{{{key}}}}}", value ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);
        }
        return template;
    }
}
