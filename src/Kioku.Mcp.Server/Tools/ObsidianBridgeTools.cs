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
        "Opens and focuses a specific note within the Obsidian application.")]
    public async Task<string> open_note_in_obsidian(
        [Description("Name or path of the note to open.")] string note)
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

        var response = await bridge.SendRequestAsync("open-file", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Note opened in Obsidian: '{found.Name}' ({found.VaultRelativePath})";
    }

    [McpServerTool, Description(
        "Returns metadata of the note currently open in Obsidian.")]
    public async Task<string> get_active_note_in_obsidian()
    {
        var response = await bridge.SendRequestAsync("get-active-note");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
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

    [McpServerTool, Description(
        "Returns the list of all notes currently open in Obsidian tabs.")]
    public async Task<string> get_open_notes_in_obsidian()
    {
        var response = await bridge.SendRequestAsync("get-open-notes");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        var data = response.Data;
        if (data is not JsonArray array || array.Count == 0)
        {
            return "[info] No notes open in Obsidian.";
        }

        var lines = new List<string>();
        foreach (var node in array)
        {
            if (node is null)
            {
                continue;
            }

            var name = node["name"]?.ToString();
            var path = node["path"]?.ToString();
            lines.Add($"- {name} ({path})");
        }

        return $"{lines.Count} note(s) open in Obsidian:\n" + string.Join("\n", lines);
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
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Command '{command_id}' executed successfully in Obsidian.";
    }

    [McpServerTool, Description("Insert text at the cursor position in the active Obsidian note.")]
    public async Task<string> insert_at_cursor(
        [Description("Text to insert at the current cursor position.")] string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "[error] The 'text' parameter cannot be empty.";
        }

        var payload = new JsonObject
        {
            ["text"] = text
        };

        var response = await bridge.SendRequestAsync("insert-at-cursor", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] Text inserted at cursor.";
    }

    [McpServerTool, Description("Replace the current text selection in the active Obsidian note.")]
    public async Task<string> replace_selection(
        [Description("Text to replace the selection with.")] string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "[error] The 'text' parameter cannot be empty.";
        }

        var payload = new JsonObject
        {
            ["text"] = text
        };

        var response = await bridge.SendRequestAsync("replace-selection", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] Selection replaced.";
    }

    [McpServerTool, Description("Create a note and open it in Obsidian. Creates the file if it does not exist.")]
    public async Task<string> create_note_ui(
        [Description("Vault-relative path of the note to create and open (e.g. 'Projects/NewNote.md').")] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "[error] The 'path' parameter cannot be empty.";
        }

        var payload = new JsonObject
        {
            ["path"] = path
        };

        var response = await bridge.SendRequestAsync("create-note-ui", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Note created and opened in Obsidian: '{path}'.";
    }

    [McpServerTool, Description("Scroll the active Obsidian note to a specific block ID (e.g. '^blockid').")]
    public async Task<string> scroll_to_block(
        [Description("Block ID to scroll to (without the ^ prefix, e.g. 'abc123').")] string block_id)
    {
        if (string.IsNullOrWhiteSpace(block_id))
        {
            return "[error] The 'block_id' parameter cannot be empty.";
        }

        var payload = new JsonObject
        {
            ["blockId"] = block_id
        };

        var response = await bridge.SendRequestAsync("scroll-to-block", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Scrolled to block '^{block_id}'.";
    }

    [McpServerTool, Description("Open a note in a new split pane in Obsidian.")]
    public async Task<string> open_in_split(
        [Description("Name or path of the note to open in a split pane.")] string note)
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

        var response = await bridge.SendRequestAsync("open-in-split", payload);
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Note opened in split pane: '{found.Name}'.";
    }

    [McpServerTool, Description("Returns the text currently selected in the active Obsidian note, if any.")]
    public async Task<string> get_selection_in_obsidian()
    {
        var response = await bridge.SendRequestAsync("get-selection");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
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

    [McpServerTool, Description("Toggles the active Obsidian note between edit mode and reading (preview) mode.")]
    public async Task<string> toggle_reading_mode()
    {
        var response = await bridge.SendRequestAsync("toggle-reading-mode");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] Reading mode toggled.";
    }

    [McpServerTool, Description("Folds all headings in the active Obsidian note (collapses all sections).")]
    public async Task<string> fold_all_headings()
    {
        var response = await bridge.SendRequestAsync("fold-all-headings");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] All headings folded.";
    }

    [McpServerTool, Description("Unfolds all headings in the active Obsidian note (expands all sections).")]
    public async Task<string> unfold_all_headings()
    {
        var response = await bridge.SendRequestAsync("unfold-all-headings");
        if (!response.Success)
        {
            return response.IsUnauthorized() ? response.Error! : $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] All headings unfolded.";
    }

    [McpServerTool, Description(
        "Returns Obsidian bridge status: whether the plugin is ready, the Obsidian and Kioku plugin versions, " +
        "and the open vault's path and name.")]
    public async Task<string> get_obsidian_status()
    {
        var ready = await bridge.SendRequestAsync("is-obsidian-ready");
        if (!ready.Success)
        {
            return ready.IsUnauthorized() ? ready.Error! : $"[error] Obsidian plugin error: {ready.Error}";
        }

        var version = await bridge.SendRequestAsync("get-app-version");
        var vaultInfo = await bridge.SendRequestAsync("get-vault-path");

        var obsidianVersion = version.Data?["obsidianVersion"]?.ToString() ?? "unknown";
        var kiokuVersion = version.Data?["kiokuVersion"]?.ToString() ?? "unknown";
        var vaultPath = vaultInfo.Data?["vaultPath"]?.ToString() ?? "unknown";
        var vaultName = vaultInfo.Data?["vaultName"]?.ToString() ?? "unknown";

        return "Obsidian bridge status:\n" +
               "   Ready: true\n" +
               $"   Obsidian version: {obsidianVersion}\n" +
               $"   Kioku plugin version: {kiokuVersion}\n" +
               $"   Vault: {vaultName} ({vaultPath})";
    }

    // Private helper

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);
}
