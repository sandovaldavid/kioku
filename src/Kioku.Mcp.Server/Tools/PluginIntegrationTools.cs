using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for Obsidian plugin integrations (Dataview, Templater, Linter).
/// All tools here require Obsidian to be open with the Kioku plugin and the target plugin enabled.
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
            return result.IsUnauthorized() ? result.Error! : $"[error] {result.Error}";
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
            return result.IsUnauthorized() ? result.Error! : $"[error] {result.Error}";
        }

        var json = result.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "null";
        return $"[ok] Template applied:\n{json}";
    }

    // lint

    [McpServerTool, Description(
        "Runs the Obsidian Linter plugin with scope='note' or scope='vault'. " +
        "For note scope, lints a specific note or the currently active note; vault scope lints all notes. " +
        "Requires Obsidian to be open with the Kioku plugin and the 'obsidian-linter' plugin enabled. " +
        "Linter fixes formatting issues according to the user's configured Linter rules.")]
    public async Task<string> lint(
        [Description("Lint scope: exactly 'note' or 'vault'.")] string scope,
        [Description("For note scope, vault-relative path of the note to lint. Leave empty to lint the currently active note.")] string note = "")
    {
        if (scope is not ("note" or "vault"))
        {
            return $"[error] Invalid lint scope '{scope}'. Valid scopes: note, vault.";
        }

        if (scope == "note")
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

            var noteResult = await bridge.SendRequestAsync("run-linter", payload);

            if (!noteResult.Success)
            {
                return noteResult.IsUnauthorized() ? noteResult.Error! : $"[error] {noteResult.Error}";
            }

            var displayName = string.IsNullOrWhiteSpace(note) ? "active note" : (ResolveNote(note)?.VaultRelativePath ?? note);
            return $"[ok] Linter executed on '{displayName}'.";
        }

        var result = await bridge.SendRequestAsync("run-linter-vault", new JsonObject());

        if (!result.Success)
        {
            return result.IsUnauthorized() ? result.Error! : $"[error] {result.Error}";
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
            return result.IsUnauthorized() ? result.Error! : $"[error] {result.Error}";
        }

        var json = result.Data?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "[]";
        return $"[ok] Installed Obsidian plugins:\n{json}";
    }

    private Note? ResolveNote(string input) => NoteHelpers.ResolveNote(input, vault);
}
