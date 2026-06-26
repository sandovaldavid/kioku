using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

[McpServerToolType]
public sealed class RestoreTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    ILogger<RestoreTools> logger)
{
    [McpServerTool, Description(
        "Reverts a note to its last committed version using git restore. " +
        "Discards all uncommitted changes. Requires the vault to be a git repository.")]
    public async Task<string> revert_note(
        [Description("Name or path of the note to revert.")] string note)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        if (!IsGitRepository())
        {
            return "[error] Not a git repository. Initialize git in the vault directory with 'git init'.";
        }

        var (restoreSuccess, _, restoreError) = RunGitCommand("restore", found.VaultRelativePath);
        if (!restoreSuccess)
        {
            return $"[error] Git restore failed: {restoreError}";
        }

        await vault.SynchronizeFileReindexAsync(found.FilePath);

        return $"[ok] Note reverted to last committed version: '{found.VaultRelativePath}'";
    }

    [McpServerTool, Description(
        "Lists notes in the trash folder that can be restored. " +
        "Shows file paths and how long ago they were deleted.")]
    public Task<string> list_deleted_notes(
        [Description("Trash folder (vault-relative). Default: '.trash'.")] string trash_folder = ".trash")
    {
        var trashPath = Path.Combine(config.VaultPath, trash_folder);
        if (!Directory.Exists(trashPath))
        {
            return Task.FromResult($"[info] Trash folder '{trash_folder}' does not exist or is empty.");
        }

        var files = Directory.GetFiles(trashPath, "*.md", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            return Task.FromResult($"[info] No deleted notes found in '{trash_folder}'.");
        }

        var sb = new StringBuilder($"[ok] {files.Length} deleted note(s) in '{trash_folder}':\n\n");
        foreach (var file in files)
        {
            var relPath = Path.GetRelativePath(config.VaultPath, file);
            var lastMod = File.GetLastWriteTimeUtc(file);
            var age = DateTimeOffset.UtcNow - lastMod;
            var ageStr = age.TotalHours < 24 ? $"{(int)age.TotalHours}h" : $"{(int)age.TotalDays}d";
            sb.AppendLine($"  {relPath} (deleted {ageStr} ago)");
        }

        return Task.FromResult(sb.ToString());
    }

    [McpServerTool, Description(
        "Restores a deleted note from the trash folder back to the vault. " +
        "Moves the file from .trash to the vault root or a specified destination folder.")]
    public async Task<string> restore_note_from_trash(
        [Description("Name or path of the note in the trash to restore.")] string note,
        [Description("Target folder to restore into (vault-relative). Defaults to vault root.")] string destination = "",
        [Description("If true, only reports what would be restored without moving the file.")] bool dry_run = false)
    {
        var trashPath = FindTrashFolder();
        if (trashPath is null)
        {
            return "[error] No trash folder found. Looked for '.trash' and '.obsidian/trash'.";
        }

        var trashFile = FindInTrash(note, trashPath);
        if (trashFile is null)
        {
            return $"[error] Note not found in trash: '{note}'";
        }

        var destPath = string.IsNullOrWhiteSpace(destination)
            ? Path.Combine(config.VaultPath, Path.GetFileName(trashFile))
            : Path.Combine(config.VaultPath, destination, Path.GetFileName(trashFile));

        if (File.Exists(destPath))
        {
            return $"[error] A note already exists at the destination: {Path.GetRelativePath(config.VaultPath, destPath)}";
        }

        if (dry_run)
        {
            var srcRel = Path.GetRelativePath(config.VaultPath, trashFile);
            var dstRel = Path.GetRelativePath(config.VaultPath, destPath);
            return $"[info] Would restore: {srcRel} → {dstRel}";
        }

        var destDir = Path.GetDirectoryName(destPath)!;
        if (!string.IsNullOrEmpty(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Move(trashFile, destPath);
        await vault.SynchronizeFileReindexAsync(destPath);

        var destRel = Path.GetRelativePath(config.VaultPath, destPath);
        return $"[ok] Note restored to: {destRel}";
    }

    [McpServerTool, Description(
        "Reverts all uncommitted changes across the entire vault using git restore. " +
        "Discards staged and unstaged changes to tracked files. Untracked files are not affected.")]
    public async Task<string> revert_all_uncommitted(
        [Description("If true, lists files that would be reverted without modifying them.")] bool dry_run = false)
    {
        if (!IsGitRepository())
        {
            return "[error] Not a git repository. Initialize git in the vault directory with 'git init'.";
        }

        var stagedDiff = RunGitCommand("diff", "--cached", "--name-only");
        var unstagedDiff = RunGitCommand("diff", "--name-only");

        var affectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (stagedDiff.success && !string.IsNullOrWhiteSpace(stagedDiff.output))
        {
            foreach (var line in stagedDiff.output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                affectedFiles.Add(line.Trim());
            }
        }

        if (unstagedDiff.success && !string.IsNullOrWhiteSpace(unstagedDiff.output))
        {
            foreach (var line in unstagedDiff.output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                affectedFiles.Add(line.Trim());
            }
        }

        if (affectedFiles.Count == 0)
        {
            return "[info] No uncommitted changes to revert.";
        }

        if (dry_run)
        {
            var sb = new StringBuilder($"[info] {affectedFiles.Count} file(s) would be reverted:\n\n");
            foreach (var f in affectedFiles)
            {
                sb.AppendLine($"  {f}");
            }
            return sb.ToString();
        }

        // Unstage staged changes, then revert working tree
        RunGitCommand("restore", "--staged", ".");
        var (restoreSuccess, _, restoreError) = RunGitCommand("restore", ".");
        if (!restoreSuccess)
        {
            return $"[error] Git restore failed: {restoreError}";
        }

        var reindexed = 0;
        foreach (var f in affectedFiles)
        {
            if (!f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var absPath = Path.Combine(config.VaultPath, f);
            if (File.Exists(absPath))
            {
                await vault.SynchronizeFileReindexAsync(absPath);
                reindexed++;
            }
        }

        return $"[ok] All uncommitted changes reverted. {reindexed} note(s) re-indexed.";
    }

    [McpServerTool, Description(
        "Restores a note to a specific git revision using git restore --source. " +
        "Requires the vault to be a git repository.")]
    public async Task<string> restore_note_version(
        [Description("Name or path of the note.")] string note,
        [Description("Git revision (commit hash, branch name, or ref like HEAD~2).")] string revision)
    {
        if (string.IsNullOrWhiteSpace(revision))
        {
            return "[error] The 'revision' parameter cannot be empty. Use a commit hash, branch name, or ref like HEAD~2.";
        }

        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        if (!IsGitRepository())
        {
            return "[error] Not a git repository.";
        }

        var (success, _, error) = RunGitCommand("restore", "--source", revision, "--", found.VaultRelativePath);
        if (!success)
        {
            return $"[error] Git restore failed: {error}";
        }

        await vault.SynchronizeFileReindexAsync(found.FilePath);

        return $"[ok] Note restored to revision '{revision}': '{found.VaultRelativePath}'";
    }

    // Private helpers

    private bool IsGitRepository()
    {
        var gitDir = Path.Combine(config.VaultPath, ".git");
        return Directory.Exists(gitDir);
    }

    private string? FindTrashFolder()
    {
        var candidates = new[] { ".trash", ".obsidian/trash" };
        foreach (var c in candidates)
        {
            var path = Path.Combine(config.VaultPath, c);
            if (Directory.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    private string? FindInTrash(string name, string trashFolder)
    {
        var nameWithExt = name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? name : name + ".md";

        // Try exact path first
        var exact = Path.Combine(trashFolder, nameWithExt);
        if (File.Exists(exact))
        {
            return exact;
        }

        // Try subfolder paths (Obsidian preserves folder structure in trash)
        var withSubfolder = Path.Combine(trashFolder, nameWithExt.TrimStart('/'));
        if (File.Exists(withSubfolder))
        {
            return withSubfolder;
        }

        // Search recursively by filename
        return Directory.GetFiles(trashFolder, "*.md", SearchOption.AllDirectories)
            .FirstOrDefault(f =>
                Path.GetFileName(f).Equals(nameWithExt, StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileNameWithoutExtension(f).Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private (bool success, string output, string error) RunGitCommand(params string[] args)
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = config.VaultPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            foreach (var arg in args)
            {
                processInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(processInfo);
            if (process is null)
            {
                return (false, "", "Failed to start git process");
            }

            var output = process.StandardOutput.ReadToEnd();
            var errorOutput = process.StandardError.ReadToEnd();

            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                return (false, "", errorOutput);
            }

            return (true, output, "");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Git command error");
            return (false, "", ex.Message);
        }
    }
}
