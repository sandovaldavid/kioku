using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for interacting with Git repositories in the vault.
/// Requires the vault to be a git repository.
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
