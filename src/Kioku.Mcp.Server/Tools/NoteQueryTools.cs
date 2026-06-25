using System.ComponentModel;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP query tools (read-only) for the Obsidian vault.
/// All operations here are read-only — they do not modify files.
/// </summary>
[McpServerToolType]
public sealed class NoteQueryTools(VaultIndexService vault, KiokuConfiguration config, EmbeddingService embedding)
{
    // read_note

    [McpServerTool, Description(
        "Reads the full content of an Obsidian note. " +
        "Accepts note name (without extension), vault-relative path, or absolute path.")]
    public async Task<string> read_note(
        [Description("Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md'")] string note)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'. Use list_notes to see available notes.";
        }

        // Re-read from disk to have the most up-to-date content
        var content = await File.ReadAllTextAsync(found.FilePath);
        return content;
    }

    // list_notes

    [McpServerTool, Description(
        "Lists all notes in the vault or a specific folder. " +
        "Returns name, relative path, tags, and modified date.")]
    public string list_notes(
        [Description("Folder to list (relative to the vault). Leave empty to list the entire vault.")] string folder = "")
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading. Wait a moment and try again.";
        }

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var sorted = notes.OrderBy(n => n.VaultRelativePath).ToList();

        if (sorted.Count == 0)
        {
            return string.IsNullOrWhiteSpace(folder)
                ? "The vault has no Markdown notes."
                : $"No notes found in folder '{folder}'.";
        }

        var lines = sorted.Select(n =>
        {
            var tags = n.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", n.Metadata.Tags)}]"
                : "";
            var modified = n.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return $"- {n.VaultRelativePath}{tags} (modified: {modified})";
        });

        return $"{sorted.Count} note(s){(string.IsNullOrWhiteSpace(folder) ? "" : $" in '{folder}'")}:\n" +
               string.Join("\n", lines);
    }

    // search_notes

    [McpServerTool, Description(
        "Searches notes in the vault by text in title, content, and tags. " +
        "Returns results ordered by relevance with a snippet of context.")]
    public string search_notes(
        [Description("Text to search. Can include multiple keywords.")] string query,
        [Description("Maximum number of results to return (default: 10).")] int max_results = 10)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return "[error] The 'query' parameter cannot be empty.";
        }

        var results = vault.Search(query, Math.Min(max_results, config.MaxSearchResults)).ToList();

        if (results.Count == 0)
        {
            return $"No notes found for: '{query}'";
        }

        var lines = results.Select((r, i) =>
        {
            var matchType = r.MatchType switch
            {
                NoteMatchType.TitleMatch => "title",
                NoteMatchType.TagMatch => "tag",
                NoteMatchType.ContentMatch => "content",
                _ => "match",
            };
            var score = (r.Score * 100).ToString("F0");
            var snippet = r.Snippet is not null ? $"\n  > {r.Snippet}" : "";
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";

            return $"{i + 1}. [{matchType}] **{r.Note.Name}**{tags} ({score}% relevance)\n   {r.Note.VaultRelativePath}{snippet}";
        });

        return $"{results.Count} result(s) for '{query}':\n\n" + string.Join("\n\n", lines);
    }

    // filter_notes

    [McpServerTool, Description(
        "Filters notes by YAML frontmatter metadata. " +
        "All parameters are optional — combined with AND.")]
    public string filter_notes(
        [Description("Filter by tag (e.g. 'project', 'ai', 'reference').")] string? tag = null,
        [Description("Filter by frontmatter status (e.g. 'draft', 'published', 'archived').")] string? status = null,
        [Description("Filter by note type (e.g. 'note', 'project', 'area').")] string? type = null,
        [Description("Minimum date in frontmatter (format: YYYY-MM-DD).")] string? date_from = null,
        [Description("Maximum date in frontmatter (format: YYYY-MM-DD).")] string? date_to = null)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        DateOnly? from = DateOnly.TryParse(date_from, out var df) ? df : null;
        DateOnly? to = DateOnly.TryParse(date_to, out var dt) ? dt : null;

        var notes = vault.FilterByMetadata(tag, status, type, from, to)
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        if (notes.Count == 0)
        {
            var filters = new List<string>();
            if (tag is not null)
            {
                filters.Add($"tag=#{tag}");
            }

            if (status is not null)
            {
                filters.Add($"status={status}");
            }

            if (type is not null)
            {
                filters.Add($"type={type}");
            }

            if (date_from is not null)
            {
                filters.Add($"since={date_from}");
            }

            if (date_to is not null)
            {
                filters.Add($"until={date_to}");
            }

            return $"No notes found with filters: {string.Join(", ", filters)}";
        }

        var lines = notes.Select(n =>
        {
            var tags = n.Metadata.Tags.Count > 0 ? $" [#{string.Join(", #", n.Metadata.Tags)}]" : "";
            var meta = new List<string>();
            if (n.Metadata.Status is not null)
            {
                meta.Add($"status: {n.Metadata.Status}");
            }

            if (n.Metadata.NoteType is not null)
            {
                meta.Add($"type: {n.Metadata.NoteType}");
            }

            if (n.Metadata.Date.HasValue)
            {
                meta.Add($"date: {n.Metadata.Date}");
            }

            var metaStr = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";
            return $"- {n.VaultRelativePath}{tags}{metaStr}";
        });

        return $"{notes.Count} note(s) found:\n" + string.Join("\n", lines);
    }

    // get_note_metadata

    [McpServerTool, Description(
        "Reads only the YAML frontmatter metadata of a note, without loading its full content. " +
        "More efficient than read_note when only metadata is needed.")]
    public string get_note_metadata(
        [Description("Name or path of the note.")] string note)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        var m = found.Metadata;
        var lines = new List<string>
        {
            $"**{found.Name}**",
            $"Path: {found.VaultRelativePath}",
            $"Modified: {found.LastModified.ToLocalTime():yyyy-MM-dd HH:mm}",
        };

        if (m.Tags.Count > 0)
        {
            lines.Add($"Tags: #{string.Join(", #", m.Tags)}");
        }

        if (m.Aliases.Count > 0)
        {
            lines.Add($"Aliases: {string.Join(", ", m.Aliases)}");
        }

        if (m.Status is not null)
        {
            lines.Add($"Status: {m.Status}");
        }

        if (m.NoteType is not null)
        {
            lines.Add($"Type: {m.NoteType}");
        }

        if (m.Date.HasValue)
        {
            lines.Add($"Date: {m.Date}");
        }

        if (m.Updated.HasValue)
        {
            lines.Add($"Updated: {m.Updated}");
        }

        if (m.ExtraFields.Count > 0)
        {
            lines.Add("Extra fields:");
            foreach (var (k, v) in m.ExtraFields)
            {
                lines.Add($"   {k}: {v}");
            }
        }

        var linkCount = found.OutgoingLinks.Count;
        if (linkCount > 0)
        {
            lines.Add($"Outgoing wikilinks: {linkCount}");
        }

        return string.Join("\n", lines);
    }

    // get_backlinks

    [McpServerTool, Description(
        "Returns all notes linking to the specified note via [[wikilinks]].")]
    public string get_backlinks(
        [Description("Name of the target note (without .md extension).")] string note_name)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        var backlinks = vault.GetBacklinks(note_name).OrderBy(n => n.Name).ToList();

        if (backlinks.Count == 0)
        {
            return $"No notes link to '{note_name}'.";
        }

        var lines = backlinks.Select(n => $"- [[{n.Name}]] → {n.VaultRelativePath}");
        return $"{backlinks.Count} note(s) link to '[[{note_name}]]':\n" + string.Join("\n", lines);
    }

    // get_outgoing_links

    [McpServerTool, Description(
        "Returns all wikilinks outgoing from the specified note.")]
    public string get_outgoing_links(
        [Description("Name or path of the note.")] string note)
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return $"[error] Note not found: '{note}'";
        }

        if (found.OutgoingLinks.Count == 0)
        {
            return $"The note '{found.Name}' does not contain outgoing links.";
        }

        var lines = found.OutgoingLinks.OrderBy(l => l).Select(l => $"- [[{l}]]");
        return $"{found.OutgoingLinks.Count} outgoing link(s) in '{found.Name}':\n" + string.Join("\n", lines);
    }

    // get_vault_stats

    [McpServerTool, Description(
        "Returns general statistics of the vault: total notes, unique tags, " +
        "folders, and index status.")]
    public string get_vault_stats()
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        var allNotes = vault.GetAllNotes().ToList();
        var allTags = allNotes.SelectMany(n => n.Metadata.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folders = allNotes.Select(n => Path.GetDirectoryName(n.VaultRelativePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        var lastModified = allNotes.OrderByDescending(n => n.LastModified).FirstOrDefault();

        return $"""
                **Kioku Vault Statistics**

                Total notes:       {allNotes.Count}
                Unique tags:       {allTags.Count}
                Folders:           {folders.Count}
                Last indexed:      {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}
                Index status:      {(vault.IsReady ? "[ok] Ready" : "[loading] Loading...")}

                Most recent note:  {lastModified?.Name ?? "N/A"} ({lastModified?.LastModified.ToLocalTime():yyyy-MM-dd HH:mm})
                Vault path:        {config.VaultPath}
                """;
    }

    // search_notes_semantic

    [McpServerTool, Description(
        "Searches notes by semantic meaning using Ollama embeddings. " +
        "Finds notes conceptually related to the query even without exact keyword matches. " +
        "Frontmatter fields (tags, status, type, date, extra fields) are included in the index. " +
        "Requires Ollama running with the configured embedding model.")]
    public async Task<string> search_notes_semantic(
        [Description("Natural language query. E.g. 'notes about stress and burnout'.")] string query,
        [Description("Maximum number of results to return (default: 10).")] int max_results = 10,
        [Description("Minimum similarity score 0.0–1.0 to include a result (default: 0.0 = no filter). Use 0.7 to keep only high-confidence matches.")] float min_score = 0f)
    {
        if (!embedding.IsAvailable)
            return $"[info] Semantic search unavailable — Ollama is not running at {config.OllamaUrl}";

        if (!vault.IsReady)
            return "[loading] The index is still loading.";

        if (string.IsNullOrWhiteSpace(query))
            return "[error] The 'query' parameter cannot be empty.";

        var queryVector = await embedding.EmbedAsync(query);
        if (queryVector is null)
            return "[error] Could not generate embedding for query.";

        var notesByPath = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);

        var results = embedding
            .Search(queryVector, Math.Min(max_results, config.MaxSearchResults), notesByPath, min_score)
            .ToList();

        if (results.Count == 0)
        {
            var threshold = min_score > 0f ? $" above {min_score:P0} similarity" : "";
            return $"No semantically similar notes found for: '{query}'{threshold}";
        }

        var lines = results.Select((r, i) =>
        {
            var score = (r.Score * 100).ToString("F0");
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";
            var snippet = BuildSemanticSnippet(r.Note.PlainText);
            var snippetStr = snippet is not null ? $"\n   > {snippet}" : "";
            return $"{i + 1}. [semantic] **{r.Note.Name}**{tags} ({score}% similarity)\n   {r.Note.VaultRelativePath}{snippetStr}";
        });

        return $"{results.Count} result(s) for '{query}':\n\n" + string.Join("\n\n", lines);
    }

    private static string? BuildSemanticSnippet(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText)) return null;
        var trimmed = plainText.Trim();
        return trimmed.Length > 200 ? trimmed[..200].Trim() + "…" : trimmed;
    }

    // Private helper

    private Note? ResolveNote(string nameOrPath)
    {
        // First try by exact path
        var byPath = vault.GetNote(nameOrPath);
        if (byPath is not null)
        {
            return byPath;
        }

        // Then by name (without extension)
        return vault.GetNoteByName(nameOrPath);
    }
}
