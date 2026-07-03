using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for Obsidian CSS snippet management.
/// Writes to .obsidian/snippets/ — does NOT use app.customCss (private API).
/// After creating/updating a snippet, call reload_css_snippets to apply changes without
/// restarting Obsidian.
/// </summary>
[McpServerToolType]
public sealed class CssThemingTools(KiokuConfiguration config, ObsidianBridgeService bridge)
{
    private string SnippetsFolder => Path.Combine(config.VaultPath, ".obsidian", "snippets");
    private string AppJsonPath => Path.Combine(config.VaultPath, ".obsidian", "app.json");

    // apply_css_snippet

    [McpServerTool, Description(
        "Creates or updates a CSS snippet file in the Obsidian vault's .obsidian/snippets/ folder. " +
        "Use Obsidian CSS variables (--color-base-00, --text-normal, etc.) for best compatibility. " +
        "After applying, call trigger_obsidian_command with 'app:reload-css-snippets' to activate it.")]
    public async Task<string> apply_css_snippet(
        [Description("Snippet filename without .css extension (e.g. 'sepia-editor', 'custom-tags').")] string name,
        [Description("Valid CSS content. Use Obsidian CSS variables for theme compatibility.")] string css_content,
        [Description(
            "If true, adds the snippet to Obsidian's enabled snippets list in app.json. " +
            "Requires 'app:reload-css-snippets' plugin command to take effect.")] bool enable = true)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "[error] Snippet name cannot be empty.";
        }

        if (string.IsNullOrWhiteSpace(css_content))
        {
            return "[error] CSS content cannot be empty.";
        }

        // Sanitize name — no path traversal
        var safeName = Path.GetFileNameWithoutExtension(name)
            .Replace("..", string.Empty)
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "[error] Invalid snippet name.";
        }

        Directory.CreateDirectory(SnippetsFolder);
        var filePath = Path.Combine(SnippetsFolder, safeName + ".css");
        var isNew = !File.Exists(filePath);

        await File.WriteAllTextAsync(filePath, css_content, Encoding.UTF8);

        var enableResult = string.Empty;
        if (enable)
        {
            enableResult = await EnableSnippetInAppJson(safeName);
        }

        var action = isNew ? "created" : "updated";
        var result = $"[ok] CSS snippet '{safeName}' {action}: .obsidian/snippets/{safeName}.css";

        if (!string.IsNullOrEmpty(enableResult))
        {
            result += "\n" + enableResult;
        }

        result += "\n   Tip: call trigger_obsidian_command with 'app:reload-css-snippets' to activate.";
        return result;
    }

    // list_css_snippets

    [McpServerTool, Description(
        "Lists all CSS snippet files in the vault's .obsidian/snippets/ folder, " +
        "showing their enabled/disabled status and a preview of their content.")]
    public async Task<string> list_css_snippets()
    {
        if (!Directory.Exists(SnippetsFolder))
        {
            return "[info] No snippets folder found (.obsidian/snippets/). " +
                   "Use apply_css_snippet to create your first snippet.";
        }

        var snippetFiles = Directory.EnumerateFiles(SnippetsFolder, "*.css").OrderBy(f => f).ToList();
        if (snippetFiles.Count == 0)
        {
            return "[info] No CSS snippets found in .obsidian/snippets/.";
        }

        // Read enabled snippets from app.json
        var enabledSnippets = await GetEnabledSnippets();

        var sb = new StringBuilder($"[ok] {snippetFiles.Count} CSS snippet(s):\n\n");

        foreach (var file in snippetFiles)
        {
            var snippetName = Path.GetFileNameWithoutExtension(file);
            var isEnabled = enabledSnippets.Contains(snippetName);
            var sizeBytes = new FileInfo(file).Length;
            var status = isEnabled ? "✓ enabled" : "○ disabled";

            sb.AppendLine($"  [{status}] {snippetName}.css ({sizeBytes} bytes)");

            // Preview first non-empty, non-comment line
            var preview = await GetCssPreview(file);
            if (!string.IsNullOrEmpty(preview))
            {
                sb.AppendLine($"    {preview}");
            }
        }

        return sb.ToString();
    }

    // remove_css_snippet

    [McpServerTool, Description(
        "Removes a CSS snippet file from the Obsidian vault's .obsidian/snippets/ folder. " +
        "Also removes it from the enabledCssSnippets list in app.json.")]
    public async Task<string> remove_css_snippet(
        [Description("Snippet name without .css extension (e.g. 'sepia-editor').")] string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "[error] Snippet name cannot be empty.";
        }

        var safeName = Path.GetFileNameWithoutExtension(name)
            .Replace("..", string.Empty)
            .Replace("/", string.Empty)
            .Replace("\\", string.Empty);

        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "[error] Invalid snippet name.";
        }

        var filePath = Path.Combine(SnippetsFolder, safeName + ".css");

        if (!File.Exists(filePath))
        {
            return $"[error] CSS snippet not found: .obsidian/snippets/{safeName}.css";
        }

        try
        {
            File.Delete(filePath);

            var removalResult = await RemoveSnippetFromAppJson(safeName);

            var result = $"[ok] CSS snippet '{safeName}' deleted: .obsidian/snippets/{safeName}.css";
            if (!string.IsNullOrEmpty(removalResult))
            {
                result += "\n" + removalResult;
            }

            return result;
        }
        catch (Exception ex)
        {
            return $"[error] Failed to delete snippet: {ex.Message}";
        }
    }

    [McpServerTool, Description(
        "Reloads CSS snippets in Obsidian so changes made with apply_css_snippet or " +
        "remove_css_snippet take effect without restarting Obsidian.")]
    public async Task<string> reload_css_snippets()
    {
        var response = await bridge.SendRequestAsync("reload-snippets");
        if (!response.Success)
        {
            return $"[error] Obsidian plugin error: {response.Error}";
        }

        return "[ok] CSS snippets reloaded.";
    }

    // Private helpers

    private async Task<string> RemoveSnippetFromAppJson(string snippetName)
    {
        try
        {
            if (!File.Exists(AppJsonPath))
            {
                return string.Empty;
            }

            var jsonContent = await File.ReadAllTextAsync(AppJsonPath, Encoding.UTF8);
            var appJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

            if (appJson is null)
            {
                return string.Empty;
            }

            List<string> enabledSnippets = [];

            if (appJson.TryGetValue("enabledCssSnippets", out var snippetsElement) &&
                snippetsElement.ValueKind == JsonValueKind.Array)
            {
                enabledSnippets = snippetsElement
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();
            }

            if (enabledSnippets.RemoveAll(s => s.Equals(snippetName, StringComparison.OrdinalIgnoreCase)) > 0)
            {
                var dict = appJson.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                dict["enabledCssSnippets"] = enabledSnippets;

                var updatedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(AppJsonPath, updatedJson, Encoding.UTF8);

                return $"   Snippet removed from enabledCssSnippets in app.json.";
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            return $"   Warning: could not update app.json — {ex.Message}";
        }
    }

    private async Task<string> EnableSnippetInAppJson(string snippetName)
    {
        try
        {
            Dictionary<string, JsonElement>? appJson = null;
            List<string> enabledSnippets;

            if (File.Exists(AppJsonPath))
            {
                var jsonContent = await File.ReadAllTextAsync(AppJsonPath, Encoding.UTF8);
                appJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

                if (appJson is not null &&
                    appJson.TryGetValue("enabledCssSnippets", out var snippetsElement) &&
                    snippetsElement.ValueKind == JsonValueKind.Array)
                {
                    enabledSnippets = snippetsElement
                        .EnumerateArray()
                        .Select(e => e.GetString() ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();
                }
                else
                {
                    enabledSnippets = [];
                }
            }
            else
            {
                enabledSnippets = [];
            }

            if (!enabledSnippets.Contains(snippetName, StringComparer.OrdinalIgnoreCase))
            {
                enabledSnippets.Add(snippetName);

                var dict = appJson is null
                    ? new Dictionary<string, object>()
                    : appJson.ToDictionary(kv => kv.Key, kv => (object)kv.Value);

                dict["enabledCssSnippets"] = enabledSnippets;

                var updatedJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(AppJsonPath, updatedJson, Encoding.UTF8);

                return $"   Snippet added to enabledCssSnippets in app.json.";
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            return $"   Warning: could not update app.json — {ex.Message}";
        }
    }

    private async Task<HashSet<string>> GetEnabledSnippets()
    {
        try
        {
            if (!File.Exists(AppJsonPath))
            {
                return [];
            }

            var jsonContent = await File.ReadAllTextAsync(AppJsonPath, Encoding.UTF8);
            var appJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);

            if (appJson is not null &&
                appJson.TryGetValue("enabledCssSnippets", out var snippetsElement) &&
                snippetsElement.ValueKind == JsonValueKind.Array)
            {
                return snippetsElement
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Return empty set on any parse error
        }

        return [];
    }

    private static async Task<string?> GetCssPreview(string filePath)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
            foreach (var line in lines.Take(10))
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrWhiteSpace(trimmed) &&
                    !trimmed.StartsWith("/*") &&
                    !trimmed.StartsWith("*") &&
                    !trimmed.StartsWith("//"))
                {
                    return trimmed.Length > 80 ? trimmed[..80] + "…" : trimmed;
                }
            }
        }
        catch
        {
            // Ignore read errors for preview
        }

        return null;
    }
}
