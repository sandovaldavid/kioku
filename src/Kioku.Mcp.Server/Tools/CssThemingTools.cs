using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP tools for Obsidian CSS snippet management.
/// Writes to .obsidian/snippets/ — does NOT use app.customCss (private API).
/// After creating/updating a snippet, use trigger_obsidian_command to apply changes without
/// restarting Obsidian.
/// </summary>
[McpServerToolType]
public sealed class CssThemingTools(
    KiokuConfiguration config,
    IVaultMutationService? mutations = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private string SnippetsFolder => Path.Combine(config.VaultPath, ".obsidian", "snippets");
    private string AppJsonPath => Path.Combine(config.VaultPath, ".obsidian", "app.json");

    [McpServerTool, Description(
        "Manages CSS snippets in the Obsidian vault's .obsidian/snippets/ folder. " +
        "action='list' lists snippets, action='apply' creates or updates one, and action='remove' deletes one. " +
        "Use Obsidian CSS variables (--color-base-00, --text-normal, etc.) for best compatibility. " +
        "After applying changes, call trigger_obsidian_command with 'app:reload-css-snippets' to activate them.")]
    public async Task<string> manage_css_snippets(
        [Description("Action to perform: 'list', 'apply', or 'remove'.")]
        string action = "list",
        [Description("Snippet filename without .css extension. Required for 'apply' and 'remove'.")]
        string? name = null,
        [Description("Valid CSS content. Required for 'apply'. Use Obsidian CSS variables for theme compatibility.")]
        string? css_content = null,
        [Description(
            "For 'apply', if true (the default), adds the snippet to Obsidian's enabledCssSnippets list in app.json. " +
            "Requires 'app:reload-css-snippets' plugin command to take effect.")]
        bool? enable = null)
    {
        var normalizedAction = action?.Trim().ToLowerInvariant();
        if (normalizedAction is not ("list" or "apply" or "remove"))
        {
            return $"[error] Invalid CSS snippet action '{action}'. Valid actions: list, apply, remove.";
        }

        if (normalizedAction == "list")
        {
            if (name is not null || css_content is not null || enable.HasValue)
            {
                return "[error] Action 'list' does not accept name, css_content, or enable parameters.";
            }

            return await ListSnippetsAsync();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return $"[error] Action '{normalizedAction}' requires a snippet name.";
        }

        if (normalizedAction == "apply")
        {
            if (string.IsNullOrWhiteSpace(css_content))
            {
                return "[error] Action 'apply' requires non-empty css_content.";
            }

            if (enable is null)
            {
                enable = true;
            }

            return await ApplySnippetAsync(name, css_content, enable.Value);
        }

        if (css_content is not null || enable.HasValue)
        {
            return "[error] Action 'remove' only accepts the name parameter.";
        }

        return await RemoveSnippetAsync(name);
    }

    private async Task<string> ApplySnippetAsync(string name, string cssContent, bool enable)
    {
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

        await WriteTextAsync(filePath, cssContent);

        var enableResult = enable ? await UpdateEnabledSnippetInAppJson(safeName, enable: true) : string.Empty;
        var operation = isNew ? "created" : "updated";
        var result = $"[ok] CSS snippet '{safeName}' {operation}: .obsidian/snippets/{safeName}.css";

        if (!string.IsNullOrEmpty(enableResult))
        {
            result += "\n" + enableResult;
        }

        result += "\n   Tip: call trigger_obsidian_command with 'app:reload-css-snippets' to activate.";
        return result;
    }

    private async Task<string> ListSnippetsAsync()
    {
        if (!Directory.Exists(SnippetsFolder))
        {
            return "[info] No snippets folder found (.obsidian/snippets/). " +
                   "Use manage_css_snippets(action='apply') to create your first snippet.";
        }

        var snippetFiles = Directory.EnumerateFiles(SnippetsFolder, "*.css").OrderBy(f => f).ToList();
        if (snippetFiles.Count == 0)
        {
            return "[info] No CSS snippets found in .obsidian/snippets/.";
        }

        var enabledSnippets = await GetEnabledSnippets();
        var sb = new StringBuilder($"[ok] {snippetFiles.Count} CSS snippet(s):\n\n");

        foreach (var file in snippetFiles)
        {
            var snippetName = Path.GetFileNameWithoutExtension(file);
            var isEnabled = enabledSnippets.Contains(snippetName);
            var sizeBytes = new FileInfo(file).Length;
            var status = isEnabled ? "✓ enabled" : "○ disabled";

            sb.AppendLine(CultureInfo.InvariantCulture, $"  [{status}] {snippetName}.css ({sizeBytes} bytes)");

            // Preview first non-empty, non-comment line
            var preview = await GetCssPreview(file);
            if (!string.IsNullOrEmpty(preview))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {preview}");
            }
        }

        return sb.ToString();
    }

    private async Task<string> RemoveSnippetAsync(string name)
    {
        // Sanitize name — no path traversal
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
            if (mutations is null)
            {
                File.Delete(filePath);
            }
            else
            {
                await mutations.DeleteAsync(filePath);
            }

            var removalResult = await UpdateEnabledSnippetInAppJson(safeName, enable: false);

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

    // Private helpers

    private async Task<string> UpdateEnabledSnippetInAppJson(string snippetName, bool enable)
    {
        try
        {
            Dictionary<string, JsonElement>? appJson = null;
            if (File.Exists(AppJsonPath))
            {
                var jsonContent = await File.ReadAllTextAsync(AppJsonPath, Encoding.UTF8);
                appJson = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(jsonContent);
            }

            if (appJson is null && !enable)
            {
                return string.Empty;
            }

            var enabledSnippets = appJson is not null &&
                                  appJson.TryGetValue("enabledCssSnippets", out var snippetsElement) &&
                                  snippetsElement.ValueKind == JsonValueKind.Array
                ? snippetsElement
                    .EnumerateArray()
                    .Select(e => e.GetString() ?? string.Empty)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList()
                : [];

            List<string> updatedSnippets;
            if (enable)
            {
                if (enabledSnippets.Contains(snippetName, StringComparer.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }

                updatedSnippets = [.. enabledSnippets, snippetName];
            }
            else
            {
                if (enabledSnippets.RemoveAll(s => s.Equals(snippetName, StringComparison.OrdinalIgnoreCase)) == 0)
                {
                    return string.Empty;
                }

                updatedSnippets = enabledSnippets;
            }

            var dict = appJson is null
                ? new Dictionary<string, object>()
                : appJson.ToDictionary(kv => kv.Key, kv => (object)kv.Value);
            dict["enabledCssSnippets"] = updatedSnippets;

            var updatedJson = JsonSerializer.Serialize(dict, JsonOptions);
            await WriteTextAsync(AppJsonPath, updatedJson);

            return enable
                ? "   Snippet added to enabledCssSnippets in app.json."
                : "   Snippet removed from enabledCssSnippets in app.json.";
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

    private async Task WriteTextAsync(string path, string content)
    {
        if (mutations is null)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(path, content, NoteHelpers.Utf8NoBom);
            return;
        }

        await mutations.WriteTextAsync(path, content);
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
                    !trimmed.StartsWith("/*", StringComparison.Ordinal) &&
                    !trimmed.StartsWith('*') &&
                    !trimmed.StartsWith("//", StringComparison.Ordinal))
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
