using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

[McpServerToolType]
public sealed class AssetTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    VaultPathPolicy? pathPolicy = null,
    IVaultMutationService? mutations = null)
{
    private readonly VaultPathPolicy _paths = pathPolicy ?? new VaultPathPolicy(config);

    [McpServerTool, Description("Find asset files (images, PDFs, Excalidraw) not referenced by any note. When dry_run=false, moves orphans to .trash/.kioku-orphans/.")]
    public async Task<string> find_orphan_assets(
        [Description("If true (default), lists orphans without moving them.")] bool dry_run = true)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var targetExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".pdf", ".excalidraw", ".canvas" };

        // Enumerate through the central policy so symlink/reparse-point directories are never
        // traversed outside the configured vault.
        var assetFiles = _paths.EnumerateVaultFiles("*", recursive: true)
            .Where(p => !IsHiddenPath(p, config.VaultPath) && targetExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .ToList();

        if (assetFiles.Count == 0)
        {
            return "[ok] No asset files found.";
        }

        // Build reference set: all filenames mentioned in notes
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allNotes = vault.GetAllNotes().ToList();

        foreach (var note in allNotes)
        {
            // Pattern: ![[filename]] or [text](filename) or [[filename.ext]]
            var matches = Regex.Matches(note.RawContent, @"!\[\[([^\]]+)\]\]|\[.*?\]\(([^)]+)\)|\[\[([^\]]+)\]\]");
            foreach (Match match in matches)
            {
                var filename = match.Groups[1].Value;
                if (string.IsNullOrEmpty(filename))
                {
                    filename = match.Groups[2].Value;
                }
                if (string.IsNullOrEmpty(filename))
                {
                    filename = match.Groups[3].Value;
                }

                if (!string.IsNullOrEmpty(filename))
                {
                    referencedFiles.Add(Path.GetFileName(filename));
                }
            }
        }

        // Find orphans: assets not in reference set
        var orphans = assetFiles
            .Where(p => !referencedFiles.Contains(Path.GetFileName(p)))
            .OrderBy(p => Path.GetRelativePath(config.VaultPath, p))
            .ToList();

        if (orphans.Count == 0)
        {
            return "[ok] No orphan assets found.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — {orphans.Count} orphan(s) found:\n\n");
            foreach (var orphan in orphans)
            {
                var relPath = Path.GetRelativePath(config.VaultPath, orphan);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {relPath}");
            }
            return sb.ToString();
        }

        // Move to trash
        var trashDir = _paths.ResolveVaultWritePath(Path.Combine(".trash", ".kioku-orphans"));
        Directory.CreateDirectory(trashDir);

        var movedCount = 0;
        foreach (var orphan in orphans)
        {
            var filename = Path.GetFileName(orphan);
            var destPath = _paths.ResolveVaultWritePath(Path.Combine(trashDir, filename));
            var move = _paths.ResolveVaultMove(orphan, destPath);
            if (mutations is null)
            {
                File.Move(move.Source, move.Destination, overwrite: true);
            }
            else
            {
                await mutations.MoveAsync(move.Source, move.Destination);
            }
            movedCount++;
        }

        return $"[ok] Moved {movedCount} orphan(s) to .trash/.kioku-orphans/.";
    }

    [McpServerTool, Description("Move scattered attachments into a target folder, optionally normalize their names, and update note references.")]
    public async Task<string> tidy_attachments(
        [Description("If true, rename all target-folder attachments as attachment-001.ext, attachment-002.ext, and so on.")] bool normalize_names = false,
        [Description("Vault-relative folder where attachments will be collected (for example, 'Attachments').")] string target_folder = "Attachments",
        [Description("If true, return the planned changes without modifying files or notes.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(target_folder))
        {
            return "[error] The 'target_folder' parameter cannot be empty.";
        }

        string targetPath;
        try
        {
            targetPath = _paths.ResolveVaultWritePath(target_folder);
        }
        catch (VaultAccessDeniedException exception)
        {
            return exception.ToToolError();
        }

        if (File.Exists(targetPath))
        {
            return $"[error] Target path is not a folder: '{target_folder}'";
        }

        var targetExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".pdf", ".excalidraw", ".canvas"
        };

        var targetFiles = Directory.Exists(targetPath)
            ? Directory.EnumerateFiles(targetPath)
                .Select(_paths.ResolveVaultReadPath)
                .Where(p => !IsHiddenPath(p, config.VaultPath) && targetExtensions.Contains(Path.GetExtension(p)))
                .ToList()
            : [];

        // Compare canonical directory boundaries rather than using a string prefix. A folder
        // named "Attachments-old" must not be treated as a child of "Attachments".
        var assetFiles = _paths.EnumerateVaultFiles("*", recursive: true)
            .Where(p => !IsHiddenPath(p, config.VaultPath) && targetExtensions.Contains(Path.GetExtension(p)))
            .Where(p => !IsPathWithin(targetPath, p))
            .OrderBy(p => ToVaultRelativePath(p, config.VaultPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var moves = new List<AssetMove>();
        var reservedNames = new HashSet<string>(
            Directory.Exists(targetPath)
                ? Directory.EnumerateFiles(targetPath).Select(path => Path.GetFileName(path)!)
                : [],
            StringComparer.OrdinalIgnoreCase);

        foreach (var file in assetFiles)
        {
            var fileName = Path.GetFileName(file);
            var destinationName = GetAvailableFileName(fileName, reservedNames);
            var destinationPath = _paths.ResolveVaultWritePath(Path.Combine(targetPath, destinationName));
            moves.Add(new AssetMove(file, destinationPath));
            reservedNames.Add(destinationName);
        }

        var filesAfterMove = targetFiles
            .Select(path => new AssetFile(path, path))
            .Concat(moves.Select(move => new AssetFile(move.NewPath, move.OldPath)))
            .OrderBy(file => Path.GetFileName(file.CurrentPath), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var renames = new List<AssetRename>();
        var referenceChanges = new List<AssetReferenceChange>();

        if (normalize_names)
        {
            foreach (var (file, index) in filesAfterMove.Select((file, index) => (file, index)))
            {
                var desiredPath = _paths.ResolveVaultWritePath(Path.Combine(targetPath,
                    $"attachment-{index + 1:D3}{Path.GetExtension(file.CurrentPath)}"));

                if (!PathsEqual(file.CurrentPath, desiredPath))
                {
                    renames.Add(new AssetRename(file.CurrentPath, desiredPath));
                }

                referenceChanges.Add(new AssetReferenceChange(
                    file.OriginalPath,
                    desiredPath));
            }
        }
        else
        {
            referenceChanges.AddRange(moves.Select(move => new AssetReferenceChange(
                move.OldPath,
                move.NewPath)));
        }

        if (moves.Count == 0 && renames.Count == 0)
        {
            return "[info] No attachment changes needed.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder(
                $"[info] dry_run=true — {moves.Count} move(s) and {renames.Count} rename(s) would be made:\n\n");
            foreach (var move in moves)
            {
                var relOldPath = ToVaultRelativePath(move.OldPath, config.VaultPath);
                var relNewPath = ToVaultRelativePath(move.NewPath, config.VaultPath);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {relOldPath} → {relNewPath}");
            }
            foreach (var rename in renames)
            {
                sb.AppendLine($"  {ToVaultRelativePath(rename.OldPath, config.VaultPath)} → " +
                    ToVaultRelativePath(rename.NewPath, config.VaultPath));
            }
            return sb.ToString();
        }

        Directory.CreateDirectory(targetPath);

        foreach (var move in moves)
        {
            var validated = _paths.ResolveVaultMove(move.OldPath, move.NewPath);
            if (mutations is null)
            {
                File.Move(validated.Source, validated.Destination);
            }
            else
            {
                await mutations.MoveAsync(validated.Source, validated.Destination);
            }
        }

        await ApplyRenamesSafelyAsync(renames, targetPath);

        var allNotes = vault.GetAllNotes().ToList();
        var updatedCount = 0;

        foreach (var note in allNotes)
        {
            var newContent = RewriteAssetReferences(
                note.RawContent,
                note.FilePath,
                referenceChanges,
                config.VaultPath);

            if (newContent != note.RawContent)
            {
                if (mutations is null)
                {
                    File.WriteAllText(note.FilePath, newContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                else
                {
                    await mutations.WriteTextAsync(note.FilePath, newContent);
                }
                updatedCount++;
            }
        }

        await vault.RebuildIndexAsync();
        return $"[ok] Tidied attachments in '{target_folder}': moved {moves.Count}, renamed {renames.Count}, updated {updatedCount} note(s).";
    }

    private static readonly Regex WikilinkPattern = new(
        @"(?<embed>!?)\[\[(?<target>[^\]\r\n|#]+?)(?<fragment>#[^\]\r\n|]*)?(?<alias>\|[^\]\r\n]*)?\]\]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MarkdownLinkPattern = new(
        @"\]\((?<destination>[^)\r\n]+)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed record AssetMove(string OldPath, string NewPath);

    private sealed record AssetFile(string CurrentPath, string OriginalPath);

    private sealed record AssetRename(string OldPath, string NewPath);

    private sealed record AssetReferenceChange(string OldPath, string NewPath);

    private static bool IsHiddenPath(string path, string vaultRoot)
    {
        var relativePath = Path.GetRelativePath(vaultRoot, path);
        return relativePath != "." && relativePath.Split(Path.DirectorySeparatorChar)
            .Any(segment => segment.StartsWith('.'));
    }

    private static bool IsPathWithin(string directory, string candidate)
    {
        var relativePath = Path.GetRelativePath(directory, candidate);
        return relativePath == "." ||
            (!Path.IsPathRooted(relativePath) &&
             relativePath != ".." &&
             !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
    }

    private static string ToVaultRelativePath(string path, string vaultRoot)
    {
        return Path.GetRelativePath(vaultRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static string GetAvailableFileName(string originalName, HashSet<string> reservedNames)
    {
        if (!reservedNames.Contains(originalName))
        {
            return originalName;
        }

        var stem = Path.GetFileNameWithoutExtension(originalName);
        var extension = Path.GetExtension(originalName);
        var counter = 1;
        var candidate = $"{stem}_{counter}{extension}";
        while (reservedNames.Contains(candidate))
        {
            counter++;
            candidate = $"{stem}_{counter}{extension}";
        }

        return candidate;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
    }

    private async Task ApplyRenamesSafelyAsync(IEnumerable<AssetRename> renames, string targetPath)
    {
        var staged = new List<(string TemporaryPath, string NewPath)>();
        foreach (var rename in renames)
        {
            var temporaryPath = _paths.ResolveVaultWritePath(
                Path.Combine(targetPath, $".kioku-tidy-{Guid.NewGuid():N}.tmp"));
            var stagedMove = _paths.ResolveVaultMove(rename.OldPath, temporaryPath);
            if (mutations is null)
            {
                File.Move(stagedMove.Source, stagedMove.Destination);
            }
            else
            {
                await mutations.MoveAsync(stagedMove.Source, stagedMove.Destination);
            }
            staged.Add((temporaryPath, rename.NewPath));
        }

        foreach (var (temporaryPath, newPath) in staged)
        {
            var finalMove = _paths.ResolveVaultMove(temporaryPath, newPath);
            if (mutations is null)
            {
                File.Move(finalMove.Source, finalMove.Destination);
            }
            else
            {
                await mutations.MoveAsync(finalMove.Source, finalMove.Destination);
            }
        }
    }

    private static string RewriteAssetReferences(
        string content,
        string notePath,
        IReadOnlyList<AssetReferenceChange> changes,
        string vaultRoot)
    {
        var rewritten = WikilinkPattern.Replace(content, match =>
        {
            var change = FindReferenceChange(match.Groups["target"].Value, notePath, changes, vaultRoot);
            if (change is null)
            {
                return match.Value;
            }

            var target = KeepTargetWhitespace(
                match.Groups["target"].Value,
                ToVaultRelativePath(change.NewPath, vaultRoot));
            return $"{match.Groups["embed"].Value}[[{target}{match.Groups["fragment"].Value}{match.Groups["alias"].Value}]]";
        });

        return MarkdownLinkPattern.Replace(rewritten, match =>
        {
            var destination = match.Groups["destination"].Value;
            var leadingWhitespace = destination.Length - destination.TrimStart().Length;
            var destinationWithoutLeadingWhitespace = destination[leadingWhitespace..];
            var separator = destinationWithoutLeadingWhitespace.IndexOfAny([' ', '\t']);
            var target = separator < 0
                ? destinationWithoutLeadingWhitespace
                : destinationWithoutLeadingWhitespace[..separator];
            var suffix = separator < 0 ? string.Empty : destinationWithoutLeadingWhitespace[separator..];
            var change = FindReferenceChange(target, notePath, changes, vaultRoot);
            if (change is null)
            {
                return match.Value;
            }

            var newTarget = ToVaultRelativePath(change.NewPath, vaultRoot);
            return $"]({destination[..leadingWhitespace]}{newTarget}{suffix})";
        });
    }

    private static AssetReferenceChange? FindReferenceChange(
        string reference,
        string notePath,
        IReadOnlyList<AssetReferenceChange> changes,
        string vaultRoot)
    {
        var normalizedReference = reference.Trim().Replace('\\', '/').TrimStart('/');
        var exact = changes.FirstOrDefault(change =>
            string.Equals(
                ToVaultRelativePath(change.OldPath, vaultRoot),
                normalizedReference,
                StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var noteDirectory = Path.GetDirectoryName(notePath)!;
        var relativeCandidate = Path.GetFullPath(Path.Combine(
            noteDirectory,
            normalizedReference.Replace('/', Path.DirectorySeparatorChar)));
        var noteRelative = changes.FirstOrDefault(change => PathsEqual(change.OldPath, relativeCandidate));
        if (noteRelative is not null)
        {
            return noteRelative;
        }

        if (!normalizedReference.Contains('/', StringComparison.Ordinal))
        {
            var sameName = changes.Where(change =>
                string.Equals(Path.GetFileName(change.OldPath), normalizedReference, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sameName.Count == 1)
            {
                return sameName[0];
            }
        }

        return null;
    }

    private static string KeepTargetWhitespace(string originalTarget, string replacement)
    {
        var leading = originalTarget.Length - originalTarget.TrimStart().Length;
        var trailing = originalTarget.Length - originalTarget.TrimEnd().Length;
        return originalTarget[..leading] + replacement +
            (trailing == 0 ? string.Empty : originalTarget[^trailing..]);
    }
}
