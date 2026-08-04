using System.ComponentModel;
using System.Text.Json;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Resources;

/// <summary>
/// MCP resources for the Obsidian vault. Unlike tools, resources let a client mount content
/// directly (e.g. as context for a conversation) without spending a tool-call round trip.
/// </summary>
[McpServerResourceType]
public sealed class NoteResources(VaultIndexService vault, KiokuConfiguration config)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerResource(UriTemplate = "kioku://note/{path}", Name = "note", MimeType = "text/markdown")]
    [Description("Full content (including frontmatter) of a note, resolved by vault-relative path or name.")]
    public string GetNote(string path)
    {
        var found = NoteHelpers.ResolveNote(path, vault);
        if (found is null)
        {
            throw new McpException($"[error] [NOT_FOUND] Note not found: '{path}'");
        }

        return found.RawContent;
    }

    [McpServerResource(UriTemplate = "kioku://vault/stats", Name = "vault-stats", MimeType = "application/json")]
    [Description("Snapshot of vault statistics: note count, tag count, folder count, index status.")]
    public string GetVaultStats()
    {
        var allNotes = vault.GetAllNotes().ToList();
        var tagCount = allNotes.SelectMany(n => n.Metadata.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var folderCount = allNotes
            .Select(n => Path.GetDirectoryName(n.VaultRelativePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(f => !string.IsNullOrEmpty(f));

        var stats = new
        {
            total_notes = allNotes.Count,
            unique_tags = tagCount,
            folders = folderCount,
            last_indexed = vault.LastIndexed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            index_ready = vault.IsReady,
            vault_path = config.VaultPath,
        };

        return JsonSerializer.Serialize(stats, JsonOptions);
    }
}
