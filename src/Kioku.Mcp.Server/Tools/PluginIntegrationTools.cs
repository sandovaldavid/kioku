using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for Obsidian plugin integrations (Dataview, Templater, Linter)
/// and Git merge conflict resolution. Plugin-dependent tools require Obsidian to be open
/// with the Kioku plugin and the target plugin enabled.
/// </summary>
[McpServerToolType]
public sealed class PluginIntegrationTools(VaultIndexService vault, ObsidianBridgeService bridge)
{
    // query_dataview

    [McpServerTool, Description(
        "Executes a Dataview DQL query via the Obsidian plugin bridge and returns results as JSON. " +
        "Requires Obsidian to be open with the Kioku plugin and Dataview plugin enabled. " +
        "Supports TABLE, LIST, TASK queries and inline expressions.")]
    public async Task<string> query_dataview(
        [Description("Dataview DQL query. Example: 'TABLE status, tags FROM \"Projects\" WHERE status = \"active\" SORT file.mtime DESC'")] string query)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "[error] 'query' is required.";
        }

        var payload = new JsonObject { ["query"] = query };
        var result = await bridge.SendRequestAsync("run-dataview-query", payload);

        if (!result.Success)
        {
            return $"[error] {result.Error}";
        }

        var json = result.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        return $"[ok] Dataview query results:\n{json}";
    }

    // apply_template

    [McpServerTool, Description(
        "Creates a new note from a Templater template via the Obsidian plugin bridge. " +
        "Requires Obsidian to be open with the Kioku plugin and Templater plugin enabled. " +
        "The template is instantiated by Templater — all template variables (tp.date, tp.file, etc.) are evaluated.")]
    public async Task<string> apply_template(
        [Description("Vault-relative path to the Templater template file. Example: 'Templates/Daily Note.md'")] string template_path,
        [Description("Optional: vault-relative path of an existing note to apply the template to.")] string target_note = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (string.IsNullOrWhiteSpace(template_path))
        {
            return "[error] 'template_path' is required.";
        }

        var resolvedTemplate = ResolveNote(template_path);
        if (resolvedTemplate is null)
        {
            return $"[error] Template not found: '{template_path}'";
        }

        var payload = new JsonObject { ["templatePath"] = resolvedTemplate.VaultRelativePath };

        if (!string.IsNullOrWhiteSpace(target_note))
        {
            var resolvedTarget = ResolveNote(target_note);
            if (resolvedTarget is null)
            {
                return $"[error] Target note not found: '{target_note}'";
            }
            payload["targetNote"] = resolvedTarget.VaultRelativePath;
        }

        var result = await bridge.SendRequestAsync("run-templater", payload);

        if (!result.Success)
        {
            return $"[error] {result.Error}";
        }

        var json = result.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        return $"[ok] Template applied:\n{json}";
    }

    // lint_note

    [McpServerTool, Description(
        "Runs the Obsidian Linter plugin on a specific note or the currently active note. " +
        "Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. " +
        "Linter fixes formatting issues according to the user's configured Linter rules.")]
    public async Task<string> lint_note(
        [Description("Vault-relative path of the note to lint. Leave empty to lint the currently active note.")] string note = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var payload = new JsonObject();
        if (!string.IsNullOrWhiteSpace(note))
        {
            var resolved = ResolveNote(note);
            if (resolved is null)
            {
                return $"[error] Note not found: '{note}'";
            }
            payload["notePath"] = resolved.VaultRelativePath;
        }

        var result = await bridge.SendRequestAsync("run-linter", payload);

        if (!result.Success)
        {
            return $"[error] {result.Error}";
        }

        var displayName = string.IsNullOrWhiteSpace(note) ? "active note" : (ResolveNote(note)?.VaultRelativePath ?? note);
        return $"[ok] Linter executed on '{displayName}'.";
    }

    // lint_vault

    [McpServerTool, Description(
        "Runs the Obsidian Linter plugin on all notes in the vault. " +
        "Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. " +
        "This is a long-running operation for large vaults.")]
    public async Task<string> lint_vault()
    {
        var result = await bridge.SendRequestAsync("run-linter-vault", new JsonObject());

        if (!result.Success)
        {
            return $"[error] {result.Error}";
        }

        return "[ok] Vault-wide linter started. Check Obsidian for progress.";
    }

    // get_installed_plugins

    [McpServerTool, Description(
        "Returns a list of all installed Obsidian plugins with their ID, name, version, author, and enabled status. " +
        "Requires Obsidian to be open with the Kioku plugin. " +
        "Use this to check if a required plugin (e.g. 'dataview', 'templater-obsidian') is available before calling plugin-dependent tools.")]
    public async Task<string> get_installed_plugins()
    {
        var result = await bridge.SendRequestAsync("get-installed-plugins", new JsonObject());

        if (!result.Success)
        {
            return $"[error] {result.Error}";
        }

        var json = result.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]";
        return $"[ok] Installed Obsidian plugins:\n{json}";
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

        var resolved = ResolveNote(note);
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

    private Note? ResolveNote(string input) => NoteHelpers.ResolveNote(input, vault);
}
