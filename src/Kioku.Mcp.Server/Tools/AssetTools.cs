using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

[McpServerToolType]
public sealed class AssetTools(VaultIndexService vault, KiokuConfiguration config)
{
    [McpServerTool, Description("Rename notes in a folder with numeric prefixes (01-, 02-, …) to define explicit ordering.")]
    public async Task<string> reorder_notes_in_folder(
        [Description("Vault-relative folder path containing the notes to reorder.")] string folder,
        [Description("If true, returns a preview without renaming files.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(folder))
        {
            return "[error] The 'folder' parameter cannot be empty.";
        }

        var notes = vault.GetNotesInFolder(folder).OrderBy(n => n.Name).ToList();

        if (notes.Count == 0)
        {
            return "[info] No notes found in folder.";
        }

        var renames = new List<(string OldName, string NewName, string OldPath, string NewPath)>();
        var parentDir = Path.GetDirectoryName(notes[0].FilePath)!;

        foreach (var (note, index) in notes.Select((n, i) => (n, i)))
        {
            var strippedName = Regex.Replace(note.Name, @"^\d+-", "");
            var newName = $"{index + 1:D2}-{strippedName}";

            if (newName != note.Name)
            {
                var destPath = Path.Combine(parentDir, $"{newName}.md");
                renames.Add((note.Name, newName, note.FilePath, destPath));
            }
        }

        if (renames.Count == 0)
        {
            return "[info] All notes are already correctly ordered.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — {renames.Count} rename(s) would be made:\n\n");
            foreach (var (oldName, newName, _, _) in renames)
            {
                sb.AppendLine($"  {oldName} → {newName}");
            }
            return sb.ToString();
        }

        foreach (var (_, _, oldPath, newPath) in renames)
        {
            File.Move(oldPath, newPath);
        }

        await vault.RebuildIndexAsync();
        return $"[ok] Renamed {renames.Count} note(s) in '{folder}'.";
    }

    [McpServerTool, Description("List all Excalidraw files in the vault: standalone .excalidraw files and Markdown notes with 'excalidraw: true' in frontmatter.")]
    public string list_excalidraw_files()
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var excalidrawFiles = new HashSet<string>();

        // Source A: .excalidraw files on filesystem
        var excalidrawPaths = Directory.EnumerateFiles(config.VaultPath, "*.excalidraw", SearchOption.AllDirectories)
            .Where(p => !IsHiddenPath(p));

        foreach (var path in excalidrawPaths)
        {
            excalidrawFiles.Add(Path.GetRelativePath(config.VaultPath, path));
        }

        // Source B: .md notes with excalidraw: true
        var excalidrawNotes = vault.GetAllNotes()
            .Where(n => n.Metadata.ExtraFields.TryGetValue("excalidraw", out var v)
                && v.Equals("true", StringComparison.OrdinalIgnoreCase));

        foreach (var note in excalidrawNotes)
        {
            excalidrawFiles.Add(note.VaultRelativePath);
        }

        if (excalidrawFiles.Count == 0)
        {
            return "[info] No Excalidraw files found.";
        }

        var sorted = excalidrawFiles.OrderBy(p => p).ToList();
        var sb = new StringBuilder($"[ok] Found {sorted.Count} Excalidraw file(s):\n\n");
        foreach (var path in sorted)
        {
            sb.AppendLine($"  {path}");
        }

        return sb.ToString();
    }

    [McpServerTool, Description("Return metadata (name, path, size, last modified) for a non-Markdown asset file in the vault.")]
    public string get_asset_metadata(
        [Description("Vault-relative path to the asset file (e.g. 'Attachments/diagram.png').")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "[error] The 'path' parameter cannot be empty.";
        }

        if (path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            return "[error] Use get_note_metadata for Markdown notes.";
        }

        var absPath = Path.Combine(config.VaultPath, path);

        if (!File.Exists(absPath))
        {
            return $"[error] File not found: '{path}'";
        }

        var info = new FileInfo(absPath);
        var humanSize = FormatFileSize(info.Length);

        var lines = new[]
        {
            $"**{info.Name}**",
            $"Path:      {path}",
            $"Extension: {info.Extension}",
            $"Size:      {humanSize} ({info.Length} bytes)",
            $"Modified:  {info.LastWriteTimeUtc:yyyy-MM-dd HH:mm} UTC"
        };

        return string.Join("\n", lines);
    }

    [McpServerTool, Description("Find asset files (images, PDFs, Excalidraw) not referenced by any note. When dry_run=false, moves orphans to .trash/.kioku-orphans/.")]
    public string find_orphan_assets(
        [Description("If true (default), lists orphans without moving them.")] bool dry_run = true)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var targetExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".pdf", ".excalidraw", ".canvas" };

        // Enumerate all candidate asset files
        var assetFiles = Directory.EnumerateFiles(config.VaultPath, "*", SearchOption.AllDirectories)
            .Where(p => !IsHiddenPath(p) && targetExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
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
                sb.AppendLine($"  {relPath}");
            }
            return sb.ToString();
        }

        // Move to trash
        var trashDir = Path.Combine(config.VaultPath, ".trash", ".kioku-orphans");
        Directory.CreateDirectory(trashDir);

        var movedCount = 0;
        foreach (var orphan in orphans)
        {
            var filename = Path.GetFileName(orphan);
            var destPath = Path.Combine(trashDir, filename);
            File.Move(orphan, destPath, overwrite: true);
            movedCount++;
        }

        return $"[ok] Moved {movedCount} orphan(s) to .trash/.kioku-orphans/.";
    }

    [McpServerTool, Description("Rename attachment files with a consistent pattern (note-slug-N.ext) and update all references in notes.")]
    public async Task<string> normalize_attachment_names(
        [Description("Asset folder path containing attachments to normalize (e.g., 'Attachments', 'Assets').")] string asset_folder,
        [Description("If true, returns a preview without renaming files.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(asset_folder))
        {
            return "[error] The 'asset_folder' parameter cannot be empty.";
        }

        var assetPath = Path.Combine(config.VaultPath, asset_folder);
        if (!Directory.Exists(assetPath))
        {
            return $"[error] Folder not found: '{asset_folder}'";
        }

        var assetFiles = Directory.EnumerateFiles(assetPath)
            .Where(p => !IsHiddenPath(p))
            .OrderBy(p => Path.GetFileName(p))
            .ToList();

        if (assetFiles.Count == 0)
        {
            return "[info] No asset files found in folder.";
        }

        var renames = new List<(string OldName, string NewName, string OldPath, string NewPath)>();

        foreach (var (file, index) in assetFiles.Select((f, i) => (f, i)))
        {
            var extension = Path.GetExtension(file);
            var newName = $"attachment-{index + 1:D3}{extension}";
            var oldName = Path.GetFileName(file);

            if (oldName != newName)
            {
                var newPath = Path.Combine(assetPath, newName);
                renames.Add((oldName, newName, file, newPath));
            }
        }

        if (renames.Count == 0)
        {
            return "[info] All attachments are already normalized.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — {renames.Count} rename(s) would be made:\n\n");
            foreach (var (oldName, newName, _, _) in renames)
            {
                sb.AppendLine($"  {oldName} → {newName}");
            }
            return sb.ToString();
        }

        // Apply renames and update references
        var renameMap = renames.ToDictionary(r => r.OldName, r => r.NewName, StringComparer.OrdinalIgnoreCase);

        foreach (var (_, _, oldPath, newPath) in renames)
        {
            File.Move(oldPath, newPath);
        }

        var allNotes = vault.GetAllNotes().ToList();
        var updatedCount = 0;

        foreach (var note in allNotes)
        {
            var newContent = note.RawContent;

            foreach (var (oldName, newName) in renameMap)
            {
                newContent = Regex.Replace(newContent, $@"!?\[\[{Regex.Escape(oldName)}\]\]", $"[[{newName}]]", RegexOptions.IgnoreCase);
                newContent = Regex.Replace(newContent, $@"\({Regex.Escape(oldName)}\)", $"({newName})", RegexOptions.IgnoreCase);
            }

            if (newContent != note.RawContent)
            {
                File.WriteAllText(note.FilePath, newContent);
                updatedCount++;
            }
        }

        await vault.RebuildIndexAsync();
        return $"[ok] Normalized {renames.Count} attachment(s) and updated {updatedCount} note(s).";
    }

    [McpServerTool, Description("Move scattered attachment files to a centralized folder and update all references in notes.")]
    public async Task<string> move_attachments_to_folder(
        [Description("Target folder path where attachments will be moved (e.g., 'Attachments').")] string target_folder,
        [Description("If true, returns a preview without moving files.")] bool dry_run = false)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(target_folder))
        {
            return "[error] The 'target_folder' parameter cannot be empty.";
        }

        var targetPath = Path.Combine(config.VaultPath, target_folder);
        var targetExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp", ".bmp", ".pdf", ".excalidraw", ".canvas" };

        // Find all asset files outside the target folder
        var assetFiles = Directory.EnumerateFiles(config.VaultPath, "*", SearchOption.AllDirectories)
            .Where(p => !IsHiddenPath(p) && targetExtensions.Contains(Path.GetExtension(p).ToLowerInvariant()))
            .Where(p => !p.StartsWith(targetPath, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (assetFiles.Count == 0)
        {
            return "[info] No scattered attachments found.";
        }

        var moves = new List<(string FileName, string OldPath, string NewPath)>();
        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(targetPath))
        {
            foreach (var file in Directory.EnumerateFiles(targetPath))
            {
                existingNames.Add(Path.GetFileName(file));
            }
        }

        foreach (var file in assetFiles)
        {
            var fileName = Path.GetFileName(file);
            var newPath = Path.Combine(targetPath, fileName);

            // Handle naming conflicts
            if (existingNames.Contains(fileName))
            {
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);
                var counter = 1;
                while (existingNames.Contains($"{nameWithoutExt}_{counter}{ext}"))
                {
                    counter++;
                }
                fileName = $"{nameWithoutExt}_{counter}{ext}";
                newPath = Path.Combine(targetPath, fileName);
            }

            moves.Add((fileName, file, newPath));
            existingNames.Add(fileName);
        }

        if (moves.Count == 0)
        {
            return "[info] All attachments are already in the target folder.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] dry_run=true — {moves.Count} move(s) would be made:\n\n");
            foreach (var (fileName, oldPath, _) in moves)
            {
                var relOldPath = Path.GetRelativePath(config.VaultPath, oldPath);
                sb.AppendLine($"  {relOldPath} → {target_folder}/{fileName}");
            }
            return sb.ToString();
        }

        // Create target folder
        Directory.CreateDirectory(targetPath);

        // Apply moves
        foreach (var (_, oldPath, newPath) in moves)
        {
            File.Move(oldPath, newPath, overwrite: true);
        }

        // Update references in notes
        var moveMap = moves.ToDictionary(m => Path.GetFileName(m.OldPath), m => m.FileName, StringComparer.OrdinalIgnoreCase);
        var allNotes = vault.GetAllNotes().ToList();
        var updatedCount = 0;

        foreach (var note in allNotes)
        {
            var newContent = note.RawContent;

            foreach (var (oldFileName, newFileName) in moveMap)
            {
                var newRelPath = $"{target_folder}/{newFileName}";
                newContent = Regex.Replace(newContent, $@"!?\[\[([^/]+/)*{Regex.Escape(oldFileName)}\]\]", $"[[{newRelPath}]]", RegexOptions.IgnoreCase);
                newContent = Regex.Replace(newContent, $@"\(([^/]+/)*{Regex.Escape(oldFileName)}\)", $"({newRelPath})", RegexOptions.IgnoreCase);
            }

            if (newContent != note.RawContent)
            {
                File.WriteAllText(note.FilePath, newContent);
                updatedCount++;
            }
        }

        await vault.RebuildIndexAsync();
        return $"[ok] Moved {moves.Count} attachment(s) to '{target_folder}' and updated {updatedCount} note(s).";
    }

    private static bool IsHiddenPath(string path)
    {
        var segments = Path.GetRelativePath(".", path).Split(Path.DirectorySeparatorChar);
        return segments.Any(s => s.StartsWith('.'));
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }
        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:F1} KB";
        }
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
