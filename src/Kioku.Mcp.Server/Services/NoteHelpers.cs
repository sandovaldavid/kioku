using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Domain;
using YamlDotNet.Core;

namespace Kioku.Mcp.Server.Services;

public static class NoteHelpers
{
    /// <summary>
    /// UTF-8 without BOM for every vault write. Obsidian/Node-authored files never carry a
    /// BOM; Encoding.UTF8 writes one by default, which would leak into touched notes.
    /// </summary>
    public static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Ensures that a candidate path remains inside the vault root after canonicalization and
    /// symbolic-link resolution. The legacy facade preserves its exact InvalidOperationException
    /// contract while the policy itself exposes VaultAccessDeniedException to new callers.
    /// </summary>
    public static string EnsureInsideVault(string vaultRoot, string candidate)
    {
        try
        {
            return VaultPathPolicy.EnsureInsideRoot(vaultRoot, candidate);
        }
        catch (VaultAccessDeniedException exception)
        {
            throw new InvalidOperationException("The requested path escapes the vault security boundary.", exception);
        }
    }

    public static Note? ResolveNote(string nameOrPath, VaultIndexService vault)
    {
        var all = vault.GetAllNotes();

        var byPath = vault.GetNote(nameOrPath);
        if (byPath is not null)
        {
            return byPath;
        }

        var normalized = nameOrPath.TrimStart('/').Replace('\\', '/');
        var byRelPath = all.SingleOrDefault(n =>
            n.VaultRelativePath.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            n.VaultRelativePath.Equals(normalized + ".md", StringComparison.OrdinalIgnoreCase));
        if (byRelPath is not null)
        {
            return byRelPath;
        }

        var nameOnly = Path.GetFileNameWithoutExtension(nameOrPath);
        if (!normalized.Contains('/'))
        {
            var byName = all.Where(n => n.Name.Equals(nameOnly, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byName.Count == 1)
            {
                return byName[0];
            }
        }

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

        return EnsureInsideVault(vaultPath, Path.Combine(vaultPath, normalized));
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
        if (extraFields is PreservedFrontmatterFields preserved)
        {
            var document = FrontmatterDocument.CreateFromFields(preserved.AllFields);
            document.SetStringList("tags", tags);
            document.SetString("type", type);
            document.SetString("status", status);
            document.SetDate("date", date, "created");
            document.SetString("domain", domain);
            document.SetDate("updated", updated, "modified");

            if (aliases is not null)
            {
                document.SetStringList("aliases", aliases);
            }
            if (cssClasses is not null)
            {
                document.SetStringList("cssclasses", cssClasses);
            }
            if (zettelId is not null)
            {
                document.SetString("zettel_id", zettelId);
            }

            return document.SerializeFrontmatter();
        }

        var extras = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (extraFields is not null)
        {
            foreach (var (key, value) in extraFields)
            {
                extras[key] = NormalizeLegacyFrontmatterValue(value);
            }
        }

        var model = new NoteFrontmatter
        {
            Tags = tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).ToList(),
            Aliases = aliases?.Where(alias => !string.IsNullOrWhiteSpace(alias)).ToList() ?? [],
            CssClasses = cssClasses?.Where(cssClass => !string.IsNullOrWhiteSpace(cssClass)).ToList() ?? [],
            NoteType = type,
            Status = status,
            Date = date,
            Updated = updated,
            ZettelId = zettelId,
            Domain = domain,
            ExtraFields = extras,
        };

        return FrontmatterDocument.Create(model).SerializeFrontmatter();
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

        try
        {
            var document = FrontmatterDocument.Parse(content);
            document.SetDate("updated", date, "modified");
            return document.Serialize();
        }
        catch (Exception exception) when (exception is YamlException or InvalidDataException)
        {
            return content;
        }
    }

    private static object NormalizeLegacyFrontmatterValue(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            return trimmed[1..^1];
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return value;
    }

    private static readonly Regex ConsecutiveHyphensRegex = new(@"-{2,}", RegexOptions.Compiled);

    public static string SanitizeFileName(string name)
    {
        char[] invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        var mapped = string.Concat(name
            .Select(c =>
            {
                if (char.IsWhiteSpace(c))
                {
                    return '-';
                }

                if (c is '‒' or '–' or '—' or '―' or '−')
                {
                    return '-';
                }

                return c;
            })
            .Where(c => !invalid.Contains(c)));

        return ConsecutiveHyphensRegex.Replace(mapped, "-").Trim('-');
    }

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

    public static string ExpandTemplateVariables(
        string template,
        IReadOnlyDictionary<string, string> variables,
        string? noteTitle = null,
        DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        var merged = new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

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
