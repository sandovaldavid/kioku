using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Reads and writes the Templater plugin's own folder-template settings
/// ({vault}/.obsidian/plugins/templater-obsidian/data.json), so Kioku can respect templates the
/// user already configured in Templater, and optionally register its own engineering templates
/// there too. Tolerant of a missing/malformed/foreign file — never throws, degrades to "nothing
/// configured" so callers can fall back to their own defaults.
/// </summary>
public static class TemplaterFolderTemplates
{
    // No BOM: this rewrites another plugin's own settings file, and Obsidian/Node's JSON.parse
    // is not guaranteed to tolerate a leading byte-order mark — .NET's Encoding.UTF8 emits one
    // by default, which the original file (written by Obsidian itself) never has.
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string SettingsPath(string vaultPath) =>
        Path.Combine(vaultPath, ".obsidian", "plugins", "templater-obsidian", "data.json");

    /// <summary>
    /// Reads Templater's configured folder→template pairs. Returns an empty list if Templater
    /// isn't installed, its "Folder Templates" feature is disabled, or the settings file can't
    /// be parsed as expected.
    /// </summary>
    public static async Task<IReadOnlyList<(string Folder, string Template)>> ReadAsync(string vaultPath)
    {
        var path = SettingsPath(vaultPath);
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = await JsonDocument.ParseAsync(stream);
            var root = doc.RootElement;

            if (!root.TryGetProperty("enable_folder_templates", out var enabledEl) || !enabledEl.GetBoolean())
            {
                return [];
            }

            if (!root.TryGetProperty("folder_templates", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return [.. arr.EnumerateArray()
                .Select(e => (
                    Folder: e.TryGetProperty("folder", out var f) ? f.GetString() ?? "" : "",
                    Template: e.TryGetProperty("template", out var t) ? t.GetString() ?? "" : ""))
                .Where(p => !string.IsNullOrWhiteSpace(p.Folder) && !string.IsNullOrWhiteSpace(p.Template))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Adds folder→template entries to Templater's own settings, never overwriting a folder the
    /// user already mapped (even to a different template) and never creating the settings file
    /// from scratch (only merges into an existing one — Templater must already be installed and
    /// have run at least once). Also flips <c>enable_folder_templates</c> on if entries were
    /// added. Returns how many new entries were added (0 if the file is missing, malformed, or
    /// every folder already had a mapping).
    /// </summary>
    public static async Task<int> RegisterFolderTemplatesAsync(
        string vaultPath, IReadOnlyList<(string Folder, string Template)> entries)
    {
        var path = SettingsPath(vaultPath);
        if (!File.Exists(path))
        {
            return 0;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(await File.ReadAllTextAsync(path, Encoding.UTF8));
        }
        catch (JsonException)
        {
            return 0;
        }

        if (root is not JsonObject obj)
        {
            return 0;
        }

        var existingFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var arr = obj["folder_templates"] as JsonArray ?? [];
        foreach (var item in arr)
        {
            if (item?["folder"]?.GetValue<string>() is { } f && !string.IsNullOrWhiteSpace(f))
            {
                existingFolders.Add(f);
            }
        }

        var added = 0;
        foreach (var (folder, template) in entries)
        {
            if (existingFolders.Contains(folder))
            {
                continue;
            }

            arr.Add(new JsonObject { ["folder"] = folder, ["template"] = template });
            added++;
        }

        if (added == 0)
        {
            return 0;
        }

        obj["folder_templates"] = arr;
        obj["enable_folder_templates"] = true;

        await File.WriteAllTextAsync(
            path,
            obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            Utf8NoBom);

        return added;
    }
}
