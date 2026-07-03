using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for interacting with Git repositories in the vault, including merge conflict
/// resolution. Requires the vault to be a git repository (except the merge-conflict tools,
/// which only scan/edit files on disk).
/// </summary>
[McpServerToolType]
public sealed class GitTools(KiokuConfiguration config, VaultIndexService vault, ILogger<GitTools> logger)
{
    [McpServerTool, Description(
        "Shows the current git status of the vault repository (modified, added, deleted files).")]
    public string get_git_status()
    {
        if (!IsGitRepository())
        {
            return "[error] Not a git repository. Initialize git in the vault directory with 'git init'.";
        }

        var (success, output, error) = RunGitCommand("status", "--porcelain");
        if (!success)
        {
            return $"[error] Git command failed: {error}";
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return "[info] Working tree is clean — no changes.";
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var modified = new List<string>();
        var added = new List<string>();
        var deleted = new List<string>();
        var untracked = new List<string>();

        foreach (var line in lines)
        {
            if (line.Length < 3)
            {
                continue;
            }

            var status = line[..2];
            var path = line[3..];

            switch (status)
            {
                case "M ":
                    modified.Add(path);
                    break;
                case " M":
                    modified.Add(path);
                    break;
                case "A ":
                    added.Add(path);
                    break;
                case "D ":
                    deleted.Add(path);
                    break;
                case "??":
                    untracked.Add(path);
                    break;
            }
        }

        var result = new List<string>
        {
            $"Git status of {config.VaultPath}:",
        };

        if (modified.Count > 0)
        {
            result.Add($"\nModified ({modified.Count}):");
            result.AddRange(modified.Select(p => $"  {p}"));
        }

        if (added.Count > 0)
        {
            result.Add($"\nAdded ({added.Count}):");
            result.AddRange(added.Select(p => $"  {p}"));
        }

        if (deleted.Count > 0)
        {
            result.Add($"\nDeleted ({deleted.Count}):");
            result.AddRange(deleted.Select(p => $"  {p}"));
        }

        if (untracked.Count > 0)
        {
            result.Add($"\nUntracked ({untracked.Count}):");
            result.AddRange(untracked.Select(p => $"  {p}"));
        }

        return string.Join("\n", result);
    }

    [McpServerTool, Description(
        "Lists the most recent git commits in the repository.")]
    public string list_git_commits(
        [Description("Maximum number of commits to return (default: 10)")] int max_count = 10)
    {
        if (!IsGitRepository())
        {
            return "[error] Not a git repository.";
        }

        if (max_count < 1)
        {
            return "[error] max_count must be at least 1.";
        }

        var (success, output, error) = RunGitCommand("log", "--oneline", $"-{max_count}");
        if (!success)
        {
            return $"[error] Git command failed: {error}";
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return "[info] No commits found.";
        }

        var commits = new List<string>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var hash = parts[0];
                var message = parts[1];
                commits.Add($"- {hash}: {message}");
            }
        }

        if (commits.Count == 0)
        {
            return "[info] No commits found.";
        }

        var result = new List<string>
        {
            $"Last {commits.Count} commit(s) in {config.VaultPath}:",
        };

        result.AddRange(commits);
        return string.Join("\n", result);
    }

    // stage_note

    [McpServerTool, Description(
        "Stages a note for commit using git add. " +
        "Prepares the file to be included in the next commit.")]
    public async Task<string> stage_note(
        [Description("Name or path of the note to stage.")] string note,
        [Description("If true, only reports what would be staged without running git add.")] bool dry_run = false)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        if (!IsGitRepository())
        {
            return "[error] Not a git repository.";
        }

        if (dry_run)
        {
            return $"[info] Would stage: {found.VaultRelativePath}";
        }

        var (success, _, error) = RunGitCommand("add", found.VaultRelativePath);
        if (!success)
        {
            return $"[error] Git add failed: {error}";
        }

        return $"[ok] Staged: {found.VaultRelativePath}";
    }

    // stage_all

    [McpServerTool, Description(
        "Stages all changes across the entire vault using git add -A. " +
        "Includes modified, deleted, and new (untracked) files.")]
    public Task<string> stage_all(
        [Description("If true, only reports what would be staged without running git add.")] bool dry_run = false)
    {
        if (!IsGitRepository())
        {
            return Task.FromResult("[error] Not a git repository.");
        }

        if (dry_run)
        {
            var (success, output, error) = RunGitCommand("status", "--porcelain");
            if (!success)
            {
                return Task.FromResult($"[error] Git command failed: {error}");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                return Task.FromResult("[info] No changes to stage.");
            }

            return Task.FromResult($"[info] Would stage {output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length} change(s)");
        }

        var (stageSuccess, _, stageError) = RunGitCommand("add", "-A");
        if (!stageSuccess)
        {
            return Task.FromResult($"[error] Git add failed: {stageError}");
        }

        return Task.FromResult("[ok] All changes staged.");
    }

    // unstage_note

    [McpServerTool, Description(
        "Unstages a previously staged note using git restore --staged. " +
        "Removes the file from the staging area without discarding changes.")]
    public async Task<string> unstage_note(
        [Description("Name or path of the note to unstage.")] string note)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        if (!IsGitRepository())
        {
            return "[error] Not a git repository.";
        }

        var (success, _, error) = RunGitCommand("restore", "--staged", found.VaultRelativePath);
        if (!success)
        {
            return $"[error] Git unstage failed: {error}";
        }

        return $"[ok] Unstaged: {found.VaultRelativePath}";
    }

    // commit_staged

    [McpServerTool, Description(
        "Commits all staged changes with the given message. " +
        "Returns an informational message if there is nothing to commit.")]
    public Task<string> commit_staged(
        [Description("Commit message describing the changes.")] string message)
    {
        if (!IsGitRepository())
        {
            return Task.FromResult("[error] Not a git repository.");
        }

        var (success, output, error) = RunGitCommand("commit", "-m", message);
        if (!success)
        {
            if (error.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("nothing added to commit", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult("[info] No staged changes to commit.");
            }

            return Task.FromResult($"[error] Git commit failed: {error}");
        }

        return Task.FromResult($"[ok] Changes committed: {message}");
    }

    // fix_merge_conflicts — reads from disk, no Obsidian required

    [McpServerTool, Description(
        "Scans all Markdown notes in the vault for Git merge conflict markers (<<<<<<<, =======, >>>>>>>). " +
        "Returns a list of affected notes with the conflicting sections. " +
        "Does not modify any files — use resolve_merge_conflict to resolve conflicts. " +
        "Does not require Obsidian to be running.")]
    public string fix_merge_conflicts(
        [Description("Folder to scan (vault-relative). Leave empty to scan the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var conflicted = notes
            .Where(n => n.RawContent.Contains("<<<<<<<", StringComparison.Ordinal))
            .Select(n =>
            {
                var conflicts = ExtractConflicts(n.RawContent);
                return (note: n, conflicts);
            })
            .Where(x => x.conflicts.Count > 0)
            .ToList();

        if (conflicted.Count == 0)
        {
            return "[ok] No Git merge conflicts found in the vault.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Found {conflicted.Count} notes with merge conflicts:");
        sb.AppendLine();

        foreach (var (note, conflicts) in conflicted)
        {
            sb.AppendLine($"## {note.VaultRelativePath} ({conflicts.Count} conflict{(conflicts.Count > 1 ? "s" : "")})");
            sb.AppendLine();

            for (var i = 0; i < conflicts.Count; i++)
            {
                var (ours, theirs) = conflicts[i];
                sb.AppendLine($"### Conflict {i + 1} (index {i})");
                sb.AppendLine("**Ours (HEAD):**");
                sb.AppendLine("```");
                sb.AppendLine(ours.Length > 500 ? ours[..500] + "..." : ours);
                sb.AppendLine("```");
                sb.AppendLine("**Theirs:**");
                sb.AppendLine("```");
                sb.AppendLine(theirs.Length > 500 ? theirs[..500] + "..." : theirs);
                sb.AppendLine("```");
                sb.AppendLine();
            }
        }

        sb.AppendLine("Use `resolve_merge_conflict(note, conflict_index, version)` to resolve each conflict.");
        return sb.ToString();
    }

    // resolve_merge_conflict — writes to disk, no Obsidian required

    [McpServerTool, Description(
        "Resolves a specific Git merge conflict in a note by choosing one version. " +
        "Use 'ours' to keep the HEAD version, 'theirs' to keep the incoming version, " +
        "or 'both' to concatenate both versions. " +
        "Does not require Obsidian to be running. " +
        "The FileSystemWatcher will automatically re-index the note after resolution.")]
    public async Task<string> resolve_merge_conflict(
        [Description("Name or vault-relative path of the note with conflicts.")] string note,
        [Description("Index of the conflict to resolve (0-based). Use -1 to resolve all conflicts at once.")] int conflict_index = -1,
        [Description("Which version to keep: 'ours' (HEAD), 'theirs' (incoming), or 'both'.")] string version = "ours")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (version is not ("ours" or "theirs" or "both"))
        {
            return "[error] 'version' must be 'ours', 'theirs', or 'both'.";
        }

        var resolved = NoteHelpers.ResolveNote(note, vault);
        if (resolved is null)
        {
            return $"[error] Note not found: '{note}'. Use fix_merge_conflicts to list affected notes.";
        }

        var content = await File.ReadAllTextAsync(resolved.FilePath);

        if (!content.Contains("<<<<<<<", StringComparison.Ordinal))
        {
            return $"[ok] No merge conflicts found in '{resolved.Name}'.";
        }

        string newContent;
        int resolvedCount;

        if (conflict_index == -1)
        {
            (newContent, resolvedCount) = ResolveAllConflicts(content, version);
        }
        else
        {
            var conflicts = ExtractConflicts(content);
            if (conflict_index < 0 || conflict_index >= conflicts.Count)
            {
                return $"[error] conflict_index {conflict_index} out of range (0–{conflicts.Count - 1}).";
            }

            (newContent, resolvedCount) = ResolveConflictAt(content, conflict_index, version);
        }

        await File.WriteAllTextAsync(resolved.FilePath, newContent);

        return $"[ok] Resolved {resolvedCount} conflict(s) in '{resolved.Name}' using '{version}' version.";
    }

    // Helpers — merge conflict parsing

    private static List<(string Ours, string Theirs)> ExtractConflicts(string content)
    {
        var conflicts = new List<(string, string)>();
        var lines = content.Split('\n');

        var state = 0; // 0=normal, 1=ours, 2=theirs
        var ours = new StringBuilder();
        var theirs = new StringBuilder();

        foreach (var line in lines)
        {
            if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                state = 1;
                ours.Clear();
                theirs.Clear();
            }
            else if (line.StartsWith("=======", StringComparison.Ordinal) && state == 1)
            {
                state = 2;
            }
            else if (line.StartsWith(">>>>>>>", StringComparison.Ordinal) && state == 2)
            {
                conflicts.Add((ours.ToString().TrimEnd('\n'), theirs.ToString().TrimEnd('\n')));
                state = 0;
                ours.Clear();
                theirs.Clear();
            }
            else if (state == 1)
            {
                ours.AppendLine(line);
            }
            else if (state == 2)
            {
                theirs.AppendLine(line);
            }
        }

        return conflicts;
    }

    private static (string NewContent, int Count) ResolveAllConflicts(string content, string version)
    {
        var count = 0;
        var safetyLimit = 1000;

        while (content.Contains("<<<<<<<", StringComparison.Ordinal) && safetyLimit-- > 0)
        {
            var (updated, resolved) = ResolveConflictAt(content, 0, version);
            if (resolved == 0)
            {
                break;
            }

            content = updated;
            count += resolved;
        }

        return (content, count);
    }

    private static (string NewContent, int Count) ResolveConflictAt(string content, int index, string version)
    {
        var lines = content.Split('\n').ToList();

        var conflictStart = -1;
        var separator = -1;
        var conflictEnd = -1;
        var conflictCount = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("<<<<<<<", StringComparison.Ordinal))
            {
                conflictCount++;
                if (conflictCount == index)
                {
                    conflictStart = i;
                }
            }
            else if (lines[i].StartsWith("=======", StringComparison.Ordinal) &&
                     conflictStart >= 0 && separator < 0 && conflictCount == index)
            {
                separator = i;
            }
            else if (lines[i].StartsWith(">>>>>>>", StringComparison.Ordinal) &&
                     separator >= 0 && conflictCount == index)
            {
                conflictEnd = i;
                break;
            }
        }

        if (conflictStart < 0 || separator < 0 || conflictEnd < 0)
        {
            return (content, 0);
        }

        var oursLines = lines.GetRange(conflictStart + 1, separator - conflictStart - 1);
        var theirsLines = lines.GetRange(separator + 1, conflictEnd - separator - 1);

        List<string> replacement = version switch
        {
            "ours" => oursLines,
            "theirs" => theirsLines,
            "both" => [.. oursLines, .. theirsLines],
            _ => oursLines
        };

        lines.RemoveRange(conflictStart, conflictEnd - conflictStart + 1);
        lines.InsertRange(conflictStart, replacement);

        return (string.Join('\n', lines), 1);
    }

    // Git utilities

    private bool IsGitRepository()
    {
        var gitDir = Path.Combine(config.VaultPath, ".git");
        return Directory.Exists(gitDir);
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
            logger.Error("Git command error: {Error}", ex.Message);
            return (false, "", ex.Message);
        }
    }
}
