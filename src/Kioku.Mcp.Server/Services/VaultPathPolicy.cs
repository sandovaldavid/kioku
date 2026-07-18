using System.Collections.ObjectModel;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Filesystem operations that may be authorized by <see cref="VaultPathPolicy"/>.
/// The values are intentionally descriptive so callers cannot accidentally use a generic
/// path resolver for a more sensitive operation.
/// </summary>
public enum VaultFileAccess
{
    Read,
    Write,
    Delete,
}

/// <summary>
/// Stable security exception used when a requested filesystem operation crosses Kioku's
/// configured boundary. The message deliberately omits candidate and root paths so MCP
/// errors cannot disclose unrelated host locations.
/// </summary>
public sealed class VaultAccessDeniedException(string operation)
    : InvalidOperationException(
        $"File-system access denied for {operation}. The requested path is outside the configured vault or an allowed external root.")
{
    public const string ErrorCode = "ACCESS_DENIED";

    public string ToToolError() => $"[error] [{ErrorCode}] {Message}";
}

/// <summary>
/// Central filesystem security boundary for Kioku. Vault-relative paths are resolved from the
/// configured vault root, absolute paths are accepted only when they resolve inside that root,
/// and external reads require both an explicit feature flag and an allowlisted root.
/// </summary>
public sealed class VaultPathPolicy
{
    private readonly string _vaultRoot;
    private readonly ReadOnlyCollection<string> _externalReadRoots;

    public VaultPathPolicy(KiokuConfiguration config)
    {
        _vaultRoot = ResolvePathWithLinks(config.VaultPath);
        AllowExternalReads = config.AllowExternalReads;
        AllowPermanentDelete = config.AllowPermanentDelete;
        _externalReadRoots = config.ExternalReadRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(ResolvePathWithLinks)
            .Distinct(GetPathComparer())
            .ToList()
            .AsReadOnly();
    }

    /// <summary>The canonical vault root used by all policy decisions.</summary>
    public string VaultRoot => _vaultRoot;

    /// <summary>Whether external reads may be evaluated against <see cref="ExternalReadRoots"/>.</summary>
    public bool AllowExternalReads { get; }

    /// <summary>Whether irreversible deletion is enabled for write tools.</summary>
    public bool AllowPermanentDelete { get; }

    /// <summary>Canonical roots from which explicitly enabled external reads are permitted.</summary>
    public IReadOnlyList<string> ExternalReadRoots => _externalReadRoots;

    public string ResolveVaultReadPath(string candidate) =>
        ResolveVaultPath(candidate, VaultFileAccess.Read);

    public string ResolveVaultWritePath(string candidate) =>
        ResolveVaultPath(candidate, VaultFileAccess.Write);

    public string ResolveVaultDeletePath(string candidate) =>
        ResolveVaultPath(candidate, VaultFileAccess.Delete);

    /// <summary>
    /// Resolves a path for an operation that must stay inside the vault. Relative paths are always
    /// vault-relative; Kioku never resolves user input relative to the server process CWD.
    /// </summary>
    public string ResolveVaultPath(string candidate, VaultFileAccess access)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("A filesystem path is required.", nameof(candidate));
        }

        var combined = Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(_vaultRoot, candidate);
        var resolved = ResolvePathWithLinks(combined);
        if (!IsWithinRoot(_vaultRoot, resolved))
        {
            throw new VaultAccessDeniedException(access.ToString().ToLowerInvariant());
        }

        return resolved;
    }

    /// <summary>
    /// Resolves a read path that may be external. Relative paths remain vault-relative. Absolute
    /// external paths are denied unless external reads are enabled and the canonical path is under
    /// one of the configured roots.
    /// </summary>
    public string ResolveExternalReadPath(string candidate)
    {
        if (!Path.IsPathRooted(candidate))
        {
            return ResolveVaultReadPath(candidate);
        }

        var resolved = ResolvePathWithLinks(candidate);
        if (IsWithinRoot(_vaultRoot, resolved))
        {
            return resolved;
        }

        if (!AllowExternalReads || !_externalReadRoots.Any(root => IsWithinRoot(root, resolved)))
        {
            throw new VaultAccessDeniedException("external read");
        }

        return resolved;
    }

    /// <summary>Validates both sides of a move before the caller performs any mutation.</summary>
    public (string Source, string Destination) ResolveVaultMove(string source, string destination) =>
        (ResolveVaultDeletePath(source), ResolveVaultWritePath(destination));

    /// <summary>
    /// Enumerates files without traversing reparse points or symbolic-link directories. Each
    /// yielded path is revalidated so a platform-specific enumeration edge cannot cross the vault.
    /// </summary>
    public IEnumerable<string> EnumerateVaultFiles(string searchPattern, bool recursive)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(_vaultRoot, searchPattern, options))
        {
            string resolved;
            try
            {
                resolved = ResolveVaultReadPath(path);
            }
            catch (VaultAccessDeniedException)
            {
                continue;
            }

            yield return resolved;
        }
    }

    public bool IsInsideVault(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        try
        {
            return IsWithinRoot(_vaultRoot, ResolvePathWithLinks(candidate));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or VaultAccessDeniedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Compatibility entry point for existing callers. All legacy EnsureInsideVault operations now
    /// share the same canonicalization and symlink-aware boundary as the injected policy.
    /// </summary>
    public static string EnsureInsideRoot(string root, string candidate)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidate))
        {
            throw new ArgumentException("Root and candidate paths are required.");
        }

        var originalRoot = Path.GetFullPath(root);
        var canonicalRoot = ResolvePathWithLinks(root);
        var combined = Path.IsPathRooted(candidate)
            ? candidate
            : Path.Combine(canonicalRoot, candidate);
        var canonicalCandidate = ResolvePathWithLinks(combined);
        if (!IsWithinRoot(canonicalRoot, canonicalCandidate))
        {
            throw new VaultAccessDeniedException("vault access");
        }

        return PathsEqual(canonicalRoot, canonicalCandidate)
            ? originalRoot
            : canonicalCandidate;
    }

    private static string ResolvePathWithLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("The path does not have a filesystem root.", nameof(path));
        var segments = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);

        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            var next = Path.Combine(current, segments[index]);
            var entry = GetExistingEntry(next);
            if (entry is null)
            {
                for (; index < segments.Length; index++)
                {
                    current = Path.Combine(current, segments[index]);
                }
                break;
            }

            current = next;
            if ((entry.Attributes & FileAttributes.ReparsePoint) == 0 && entry.LinkTarget is null)
            {
                continue;
            }

            FileSystemInfo? resolvedTarget;
            try
            {
                resolvedTarget = entry.ResolveLinkTarget(returnFinalTarget: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new VaultAccessDeniedException("symbolic-link resolution");
            }

            if (resolvedTarget is null)
            {
                throw new VaultAccessDeniedException("symbolic-link resolution");
            }

            current = Path.GetFullPath(resolvedTarget.FullName);
        }

        return Path.GetFullPath(current);
    }

    private static FileSystemInfo? GetExistingEntry(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        if (File.Exists(path))
        {
            return new FileInfo(path);
        }

        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path)
                : new FileInfo(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool IsWithinRoot(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static StringComparer GetPathComparer() =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}