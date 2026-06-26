using System.ComponentModel;
using System.Diagnostics;
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

    // Private helpers

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
            logger.LogError(ex, "Git command error");
            return (false, "", ex.Message);
        }
    }
}
