using System.ComponentModel;
using System.Text.Json.Nodes;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools to interact directly with the Obsidian visual interface (via the WebSocket bridge).
/// </summary>
[McpServerToolType]
public sealed class ObsidianBridgeTools(ObsidianBridgeService bridge, VaultIndexService vault)
{
    [McpServerTool, Description(
        "Opens and focuses a specific note within Obsidian. Set split=true to open it in a new split pane.")]
    public async Task<string> open_note_in_obsidian(
        [Description("Name or path of the note to open.")] string note,
        [Description("Open the note in a new split pane instead of the current pane.")] bool split = false)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found on local disk: '{note}'";
        }

        var payload = new JsonObject
        {
            ["path"] = found.VaultRelativePath
        };

        var response = await bridge.SendRequestAsync(split ? "open-in-split" : "open-file", payload);
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        return split
            ? $"[ok] Note opened in split pane: '{found.Name}'."
            : $"[ok] Note opened in Obsidian: '{found.Name}' ({found.VaultRelativePath})";
    }

    [McpServerTool, Description(
        "Returns a snapshot of Obsidian's bridge status, active note, open notes, and selection. " +
        "Individual sections may report errors if a bridge request fails.")]
    public async Task<string> get_obsidian_state()
    {
        // Sequential on purpose: the bridge multiplexes one ClientWebSocket, and
        // ClientWebSocket allows only a single outstanding SendAsync — concurrent
        // sends throw and tear down the connection.
        var sections = new[]
        {
            await GetObsidianStatusAsync(),
            await GetActiveNoteAsync(),
            await GetOpenNotesAsync(),
            await GetSelectionAsync(),
        };

        return string.Join("\n\n", sections);
    }

    [McpServerTool, Description(
        "Triggers an internal Obsidian command by its unique identifier (command ID).")]
    public async Task<string> trigger_obsidian_command(
        [Description("Unique ID of the command (e.g., 'app:toggle-left-sidebar', 'workspace:close-others').")] string command_id)
    {
        var payload = new JsonObject
        {
            ["commandId"] = command_id
        };

        var response = await bridge.SendRequestAsync("trigger-command", payload);
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        return $"[ok] Command '{command_id}' executed successfully in Obsidian.";
    }

    [McpServerTool, Description(
        "Edits the active Obsidian note. mode must be 'insert_at_cursor' or 'replace_selection'.")]
    public async Task<string> edit_in_obsidian(
        [Description("Text to insert or use as the replacement.")] string text,
        [Description("'insert_at_cursor' to insert at the cursor, or 'replace_selection' to replace the current selection.")] string mode)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "[error] The 'text' parameter cannot be empty.";
        }

        var normalizedMode = mode?.Trim().ToLowerInvariant();
        var command = normalizedMode switch
        {
            "insert_at_cursor" => "insert-at-cursor",
            "replace_selection" => "replace-selection",
            _ => null
        };

        if (command is null)
        {
            return $"[error] Unknown mode '{mode}'. Use 'insert_at_cursor' or 'replace_selection'.";
        }

        var payload = new JsonObject
        {
            ["text"] = text
        };

        var response = await bridge.SendRequestAsync(command, payload);
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        return normalizedMode == "insert_at_cursor"
            ? "[ok] Text inserted at cursor."
            : "[ok] Selection replaced.";
    }

    private async Task<string> GetActiveNoteAsync()
    {
        var response = await bridge.SendRequestAsync("get-active-note");
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        var data = response.Data;
        if (data is null || data.GetValueKind() == System.Text.Json.JsonValueKind.Null)
        {
            return "[info] No active note open in Obsidian at this moment.";
        }

        var path = data["path"]?.ToString();
        var name = data["name"]?.ToString();
        var status = data["status"]?.ToString();

        var tagsList = new List<string>();
        if (data["tags"] is JsonArray tagsArray)
        {
            foreach (var node in tagsArray)
            {
                if (node is not null)
                {
                    tagsList.Add(node.ToString());
                }
            }
        }

        var tagsStr = tagsList.Count > 0 ? $" [#{string.Join(", #", tagsList)}]" : "";
        var statusStr = status is not null ? $" (status: {status})" : "";

        return $"Active note in Obsidian:\n" +
               $"   Name: {name}\n" +
               $"   Path: {path}{tagsStr}{statusStr}";
    }

    private async Task<string> GetOpenNotesAsync()
    {
        var response = await bridge.SendRequestAsync("get-open-notes");
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        var data = response.Data;
        if (data is not JsonArray array || array.Count == 0)
        {
            return "[info] No notes open in Obsidian.";
        }

        var lines = new List<string>();
        foreach (var node in array)
        {
            if (node is not null)
            {
                var name = node["name"]?.ToString();
                var path = node["path"]?.ToString();
                lines.Add($"- {name} ({path})");
            }
        }

        return $"{lines.Count} note(s) open in Obsidian:\n" + string.Join("\n", lines);
    }

    private async Task<string> GetSelectionAsync()
    {
        var response = await bridge.SendRequestAsync("get-selection");
        if (!response.Success)
        {
            return FormatBridgeError(response);
        }

        var data = response.Data;
        var hasSelection = data?["hasSelection"]?.GetValue<bool?>() ?? false;
        if (!hasSelection)
        {
            return "[info] No text selected in Obsidian.";
        }

        var selection = data?["selection"]?.ToString();
        var length = data?["length"]?.ToString();
        return $"Selected text ({length} chars):\n{selection}";
    }

    private async Task<string> GetObsidianStatusAsync()
    {
        var ready = await bridge.SendRequestAsync("is-obsidian-ready");
        if (!ready.Success)
        {
            return FormatBridgeError(ready);
        }

        var version = await bridge.SendRequestAsync("get-app-version");
        var vaultInfo = await bridge.SendRequestAsync("get-vault-path");

        var obsidianVersion = version.Success
            ? version.Data?["obsidianVersion"]?.ToString() ?? "unknown"
            : FormatBridgeError(version);
        var kiokuVersion = version.Success
            ? version.Data?["kiokuVersion"]?.ToString() ?? "unknown"
            : "unknown";
        var vaultPath = vaultInfo.Success
            ? vaultInfo.Data?["vaultPath"]?.ToString() ?? "unknown"
            : FormatBridgeError(vaultInfo);
        var vaultName = vaultInfo.Success
            ? vaultInfo.Data?["vaultName"]?.ToString() ?? "unknown"
            : "unknown";

        return "Obsidian bridge status:\n" +
               "   Ready: true\n" +
               $"   Obsidian version: {obsidianVersion}\n" +
               $"   Kioku plugin version: {kiokuVersion}\n" +
               $"   Vault: {vaultName} ({vaultPath})";
    }

    private static string FormatBridgeError(BridgeResponse response) =>
        response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);
}
