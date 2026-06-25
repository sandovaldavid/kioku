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
public sealed class GitTools(KiokuConfiguration config, ILogger<GitTools> logger)
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

        var (success, output, error) = RunGitCommand("log", $"--oneline -n {max_count}");
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
