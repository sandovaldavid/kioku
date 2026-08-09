using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>Classification returned by the canonical vault wikilink resolver.</summary>
public enum VaultLinkResolutionStatus
{
    Resolved,
    Ambiguous,
    Missing,
    Malformed,
}

/// <summary>
/// Result of resolving a note lookup or wikilink target. Resolved targets can refer to an
/// indexed <see cref="Note"/> or to a real Markdown file excluded from the runtime index.
/// </summary>
public sealed record VaultLinkResolution(
    VaultLinkResolutionStatus Status,
    string RawTarget,
    string Target,
    string? Fragment,
    Note? Note,
    string? CanonicalTargetPath)
{
    public bool IsResolved => Status == VaultLinkResolutionStatus.Resolved;
}

/// <summary>
/// Canonical resolver for note identities and Obsidian wikilink targets. All path candidates are
/// normalized through <see cref="VaultPathPolicy"/> so source-relative links cannot escape the
/// configured vault boundary.
/// </summary>
public sealed class VaultLinkResolver(
    VaultPathPolicy paths,
    Func<IReadOnlyCollection<Note>> getNotes)
{
    public VaultLinkResolution ResolveNote(string rawTarget) => ResolveCore(null, rawTarget);

    public VaultLinkResolution Resolve(Note source, string rawTarget) => ResolveCore(source, rawTarget);

    private VaultLinkResolution ResolveCore(Note? source, string rawTarget)
    {
        if (!TryNormalizeRawTarget(rawTarget, out var normalized))
        {
            return Malformed(rawTarget);
        }

        var literal = ResolveLiteral(source, rawTarget, normalized);
        if (literal.Status != VaultLinkResolutionStatus.Missing)
        {
            return literal;
        }

        var hashIndex = normalized.IndexOf('#');
        if (hashIndex < 0)
        {
            return literal;
        }

        var target = normalized[..hashIndex].Trim();
        var fragment = normalized[hashIndex..].Trim();
        if (fragment.Length <= 1)
        {
            return Malformed(rawTarget, target, fragment);
        }

        if (target.Length == 0)
        {
            return source is null
                ? Missing(rawTarget, target, fragment)
                : Resolved(rawTarget, target, fragment, source, CanonicalPath(source));
        }

        var withoutFragment = ResolveLiteral(source, rawTarget, target);
        return withoutFragment with { Fragment = fragment };
    }

    private VaultLinkResolution ResolveLiteral(Note? source, string rawTarget, string target)
    {
        var notes = getNotes();
        var normalized = target.Replace('\\', '/').Trim();
        if (normalized.Length == 0)
        {
            return Malformed(rawTarget);
        }

        if (source is null && Path.IsPathRooted(target))
        {
            return ResolveFilesystemPath(rawTarget, normalized, target, notes);
        }

        if (source is not null && normalized.StartsWith('/', StringComparison.Ordinal))
        {
            return ResolveFilesystemPath(rawTarget, normalized, normalized.TrimStart('/'), notes);
        }

        if (normalized.StartsWith("./", StringComparison.Ordinal) ||
            normalized.StartsWith("../", StringComparison.Ordinal) ||
            normalized is "." or "..")
        {
            if (source is null)
            {
                return Malformed(rawTarget, normalized);
            }

            var sourceDirectory = Path.GetDirectoryName(source.FilePath) ?? paths.VaultRoot;
            return ResolveFilesystemPath(
                rawTarget,
                normalized,
                Path.Combine(sourceDirectory, normalized.Replace('/', Path.DirectorySeparatorChar)),
                notes);
        }

        if (normalized.Contains('/'))
        {
            var byPath = ResolveFilesystemPath(rawTarget, normalized, normalized, notes);
            if (byPath.Status != VaultLinkResolutionStatus.Missing)
            {
                return byPath;
            }
        }
        else
        {
            var basename = Path.GetFileNameWithoutExtension(normalized);
            var byName = notes
                .Where(note => note.Name.Equals(basename, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byName.Count == 1)
            {
                return Resolved(rawTarget, normalized, null, byName[0], CanonicalPath(byName[0]));
            }

            if (byName.Count > 1)
            {
                return Ambiguous(rawTarget, normalized);
            }
        }

        var byAlias = notes
            .Where(note => note.Metadata.Aliases.Any(alias =>
                alias.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (byAlias.Count == 1)
        {
            return Resolved(rawTarget, normalized, null, byAlias[0], CanonicalPath(byAlias[0]));
        }

        if (byAlias.Count > 1)
        {
            return Ambiguous(rawTarget, normalized);
        }

        if (!normalized.Contains('/'))
        {
            return ResolveExcludedBasename(rawTarget, normalized, notes);
        }

        return Missing(rawTarget, normalized);
    }

    private VaultLinkResolution ResolveFilesystemPath(
        string rawTarget,
        string target,
        string candidate,
        IReadOnlyCollection<Note> notes)
    {
        string resolved;
        try
        {
            var withExtension = candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : candidate + ".md";
            resolved = paths.ResolveVaultReadPath(withExtension);
        }
        catch (Exception exception) when (
            exception is VaultAccessDeniedException or ArgumentException or IOException or NotSupportedException)
        {
            return Malformed(rawTarget, target);
        }

        var indexed = notes.FirstOrDefault(note =>
            note.FilePath.Equals(resolved, StringComparison.OrdinalIgnoreCase));
        if (indexed is not null)
        {
            return Resolved(rawTarget, target, null, indexed, CanonicalPath(indexed));
        }

        if (!File.Exists(resolved))
        {
            return Missing(rawTarget, target);
        }

        return Resolved(
            rawTarget,
            target,
            null,
            null,
            CanonicalPath(Path.GetRelativePath(paths.VaultRoot, resolved)));
    }

    private VaultLinkResolution ResolveExcludedBasename(
        string rawTarget,
        string target,
        IReadOnlyCollection<Note> notes)
    {
        var basename = Path.GetFileNameWithoutExtension(target);
        var indexedPaths = notes.Select(note => note.FilePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = paths.EnumerateVaultFiles("*.md", recursive: true)
            .Where(path => !indexedPaths.Contains(path))
            .Where(path => Path.GetFileNameWithoutExtension(path)
                .Equals(basename, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        return matches.Count switch
        {
            0 => Missing(rawTarget, target),
            1 => Resolved(
                rawTarget,
                target,
                null,
                null,
                CanonicalPath(Path.GetRelativePath(paths.VaultRoot, matches[0]))),
            _ => Ambiguous(rawTarget, target),
        };
    }

    private static bool TryNormalizeRawTarget(string rawTarget, out string normalized)
    {
        normalized = rawTarget?.Trim() ?? string.Empty;
        if (normalized.Length == 0 ||
            normalized.IndexOfAny(['\0', '\r', '\n']) >= 0 ||
            normalized.Contains("[[", StringComparison.Ordinal) ||
            normalized.Contains("]]", StringComparison.Ordinal))
        {
            return false;
        }

        if (normalized.StartsWith("[[", StringComparison.Ordinal) &&
            normalized.EndsWith("]]", StringComparison.Ordinal))
        {
            normalized = normalized[2..^2].Trim();
        }

        var pipeIndex = normalized.IndexOf('|');
        if (pipeIndex >= 0)
        {
            normalized = normalized[..pipeIndex].Trim();
        }

        return normalized.Length > 0;
    }

    private static string CanonicalPath(Note note) => CanonicalPath(note.VaultRelativePath);

    private static string CanonicalPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^3]
            : normalized;
    }

    private static VaultLinkResolution Resolved(
        string rawTarget,
        string target,
        string? fragment,
        Note? note,
        string canonicalTargetPath) =>
        new(VaultLinkResolutionStatus.Resolved, rawTarget, target, fragment, note, canonicalTargetPath);

    private static VaultLinkResolution Ambiguous(string rawTarget, string target, string? fragment = null) =>
        new(VaultLinkResolutionStatus.Ambiguous, rawTarget, target, fragment, null, null);

    private static VaultLinkResolution Missing(string rawTarget, string target, string? fragment = null) =>
        new(VaultLinkResolutionStatus.Missing, rawTarget, target, fragment, null, null);

    private static VaultLinkResolution Malformed(
        string rawTarget,
        string target = "",
        string? fragment = null) =>
        new(VaultLinkResolutionStatus.Malformed, rawTarget, target, fragment, null, null);
}
