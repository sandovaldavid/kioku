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
            return $"[error] Obsidian plugin error: {response.Error}";
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
            return $"[error] Obsidian plugin error: {response.Error}";
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
            return $"[error] Obsidian plugin error: {response.Error}";
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
            return $"[error] Obsidian plugin error: {response.Error}";
        }

        return $"[ok] Command '{command_id}' executed successfully in Obsidian.";
    }

    // Private helper

    private Note? ResolveNote(string nameOrPath)
    {
        var byPath = vault.GetNote(nameOrPath);
        if (byPath is not null)
        {
            return byPath;
        }

        return vault.GetNoteByName(nameOrPath);
    }
}
