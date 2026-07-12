using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP query tools (read-only) for the Obsidian vault.
/// All operations here are read-only — they do not modify files.
/// </summary>
[McpServerToolType]
public sealed class NoteQueryTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    EmbeddingService embedding,
    HybridSearchService hybrid,
    VaultConfigService vaultConfig,
    MetricsService? metrics = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    private static void Count(string name, MetricsService? metrics) => metrics?.RecordToolCall(name);

    private static bool IsJsonFormat(string format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    // read_note

    [McpServerTool, Description(
        "Reads the full content of an Obsidian note. " +
        "Accepts note name (without extension), vault-relative path, or absolute path. " +
        "Use format='json' to receive a structured response.")]
    public async Task<string> read_note(
        [Description("Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md'")] string note,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(read_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Note not found: '{note}'" })
                : KiokuError.NotFound($"Note not found: '{note}'. Use list_notes to see available notes.");
        }

        // Re-read from disk to have the most up-to-date content
        var content = await File.ReadAllTextAsync(found.FilePath);
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                name = found.Name,
                path = found.VaultRelativePath,
                content,
            });
        }

        return content;
    }

    // list_notes

    [McpServerTool, Description(
        "Lists notes in the vault or a specific folder. " +
        "Supports pagination via offset and limit. " +
        "Returns name, relative path, tags, and modified date. " +
        "Use format='json' to receive a structured response.")]
    public string list_notes(
        [Description("Folder to list (relative to the vault). Leave empty to list the entire vault.")] string folder = "",
        [Description("Maximum number of notes to return (default: 50, capped by KIOKU_MAX_RESULTS).")] int limit = 50,
        [Description("Number of notes to skip for pagination.")] int offset = 0,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(list_notes), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading. Wait a moment and try again.";
        }

        if (offset < 0)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'offset' must be 0 or greater." })
                : KiokuError.InvalidArgument("'offset' must be 0 or greater.");
        }

        if (limit <= 0)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'limit' must be greater than 0." })
                : KiokuError.InvalidArgument("'limit' must be greater than 0.");
        }

        limit = Math.Min(limit, config.MaxSearchResults);

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var sorted = notes.OrderBy(n => n.VaultRelativePath).ToList();
        var total = sorted.Count;
        var page = sorted.Skip(offset).Take(limit).ToList();

        if (page.Count == 0)
        {
            var emptyMessage = string.IsNullOrWhiteSpace(folder)
                ? "The vault has no Markdown notes (or the requested page is empty)."
                : $"No notes found in folder '{folder}' (or the requested page is empty).";
            return IsJsonFormat(format)
                ? ToJson(new { total = 0, offset, limit, folder, notes = Array.Empty<object>() })
                : emptyMessage;
        }

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                total,
                offset,
                limit,
                folder,
                notes = page.Select(n => new
                {
                    name = n.Name,
                    path = n.VaultRelativePath,
                    tags = n.Metadata.Tags,
                    modified = n.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                }),
            });
        }

        var lines = page.Select(n =>
        {
            var tags = n.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", n.Metadata.Tags)}]"
                : "";
            var modified = n.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            return $"- {n.VaultRelativePath}{tags} (modified: {modified})";
        });

        var end = Math.Min(offset + page.Count, total);
        return $"Showing {offset + 1}-{end} of {total} note(s){(string.IsNullOrWhiteSpace(folder) ? "" : $" in '{folder}'")}:\n" +
               string.Join("\n", lines);
    }

    // search_notes

    [McpServerTool, Description(
        "Searches notes in the vault by text in title, content, and tags. " +
        "Returns results ordered by relevance with a snippet of context. " +
        "Use format='json' to receive a structured response.")]
    public string search_notes(
        [Description("Text to search. Can include multiple keywords.")] string query,
        [Description("Maximum number of results to return (default: 10).")] int max_results = 10,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(search_notes), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "The 'query' parameter cannot be empty." })
                : KiokuError.InvalidArgument("The 'query' parameter cannot be empty.");
        }

        var results = vault.Search(query, Math.Min(max_results, config.MaxSearchResults)).ToList();

        if (results.Count == 0)
        {
            return IsJsonFormat(format)
                ? ToJson(new { query, results = Array.Empty<object>() })
                : $"No notes found for: '{query}'";
        }

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                query,
                results = results.Select((r, i) => new
                {
                    rank = i + 1,
                    name = r.Note.Name,
                    path = r.Note.VaultRelativePath,
                    score = r.Score,
                    match_type = r.MatchType switch
                    {
                        NoteMatchType.TitleMatch => "title",
                        NoteMatchType.TagMatch => "tag",
                        NoteMatchType.ContentMatch => "content",
                        _ => "match",
                    },
                    snippet = r.Snippet,
                    tags = r.Note.Metadata.Tags,
                }),
            });
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
        "All parameters are optional — combined with AND. " +
        "Use format='json' to receive a structured response.")]
    public string filter_notes(
        [Description("Filter by tag (e.g. 'project', 'ai', 'reference').")] string? tag = null,
        [Description("Filter by frontmatter status (e.g. 'draft', 'published', 'archived').")] string? status = null,
        [Description("Filter by note type (e.g. 'note', 'project', 'area').")] string? type = null,
        [Description("Minimum date in frontmatter (format: YYYY-MM-DD).")] string? date_from = null,
        [Description("Maximum date in frontmatter (format: YYYY-MM-DD).")] string? date_to = null,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(filter_notes), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading.";
        }

        DateOnly? from = DateOnly.TryParse(date_from, out var df) ? df : null;
        DateOnly? to = DateOnly.TryParse(date_to, out var dt) ? dt : null;

        var notes = vault.FilterByMetadata(tag, status, type, from, to)
            .OrderBy(n => n.VaultRelativePath)
            .ToList();

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                filters = new
                {
                    tag,
                    status,
                    type,
                    date_from,
                    date_to,
                },
                count = notes.Count,
                notes = notes.Select(n => new
                {
                    name = n.Name,
                    path = n.VaultRelativePath,
                    tags = n.Metadata.Tags,
                    status = n.Metadata.Status,
                    type = n.Metadata.NoteType,
                    date = n.Metadata.Date?.ToString("yyyy-MM-dd"),
                }),
            });
        }

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
        "More efficient than read_note when only metadata is needed. " +
        "Use format='json' to receive a structured response.")]
    public string get_note_metadata(
        [Description("Name or path of the note.")] string note,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(get_note_metadata), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Note not found: '{note}'" })
                : KiokuError.NotFound($"Note not found: '{note}'");
        }

        var m = found.Metadata;
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                name = found.Name,
                path = found.VaultRelativePath,
                modified = found.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                tags = m.Tags,
                aliases = m.Aliases,
                status = m.Status,
                type = m.NoteType,
                date = m.Date?.ToString("yyyy-MM-dd"),
                updated = m.Updated?.ToString("yyyy-MM-dd"),
                extra_fields = m.ExtraFields,
                outgoing_links = found.OutgoingLinks,
            });
        }

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
        "Returns all notes linking to the specified note via [[wikilinks]]. " +
        "Use format='json' to receive a structured response.")]
    public string get_backlinks(
        [Description("Name of the target note (without .md extension).")] string note_name,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(get_backlinks), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading.";
        }

        var backlinks = vault.GetBacklinks(note_name).OrderBy(n => n.Name).ToList();

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                note_name,
                count = backlinks.Count,
                notes = backlinks.Select(n => new
                {
                    name = n.Name,
                    path = n.VaultRelativePath,
                }),
            });
        }

        if (backlinks.Count == 0)
        {
            return $"No notes link to '{note_name}'.";
        }

        var lines = backlinks.Select(n => $"- [[{n.Name}]] → {n.VaultRelativePath}");
        return $"{backlinks.Count} note(s) link to '[[{note_name}]]':\n" + string.Join("\n", lines);
    }

    // get_outgoing_links

    [McpServerTool, Description(
        "Returns all wikilinks outgoing from the specified note. " +
        "Use format='json' to receive a structured response.")]
    public string get_outgoing_links(
        [Description("Name or path of the note.")] string note,
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        var found = ResolveNote(note);
        if (found is null)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Note not found: '{note}'" })
                : KiokuError.NotFound($"Note not found: '{note}'");
        }

        var links = found.OutgoingLinks.OrderBy(l => l).ToList();
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                name = found.Name,
                path = found.VaultRelativePath,
                count = links.Count,
                links,
            });
        }

        if (links.Count == 0)
        {
            return $"The note '{found.Name}' does not contain outgoing links.";
        }

        var lines = links.Select(l => $"- [[{l}]]");
        return $"{links.Count} outgoing link(s) in '{found.Name}':\n" + string.Join("\n", lines);
    }

    // get_vault_stats

    [McpServerTool, Description(
        "Returns general statistics of the vault: total notes, unique tags, " +
        "folders, and index status. " +
        "Use format='json' to receive a structured response.")]
    public string get_vault_stats(
        [Description("Output format: 'text' (default) or 'json'.")] string format = "text")
    {
        Count(nameof(get_vault_stats), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading.";
        }

        var allNotes = vault.GetAllNotes().ToList();
        var allTags = allNotes.SelectMany(n => n.Metadata.Tags).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folders = allNotes.Select(n => Path.GetDirectoryName(n.VaultRelativePath) ?? "")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();

        var lastModified = allNotes.OrderByDescending(n => n.LastModified).FirstOrDefault();

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                total_notes = allNotes.Count,
                unique_tags = allTags.Count,
                folders = folders.Count,
                last_indexed = vault.LastIndexed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                index_ready = vault.IsReady,
                most_recent_note = lastModified is null ? null : new
                {
                    name = lastModified.Name,
                    modified = lastModified.LastModified.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                },
                vault_path = config.VaultPath,
            });
        }

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

    // Default similarity floor for semantic results: filters unrelated notes that cosine
    // similarity still scores in the low range. Tune against a golden set with the
    // Kioku.Eval runner (docs/retrieval-eval.md); pass min_score=0 to disable filtering.
    private const float DefaultSemanticMinScore = 0.4f;

    [McpServerTool, Description(
        "Searches notes by semantic meaning using Ollama embeddings. " +
        "Finds notes conceptually related to the query even without exact keyword matches. " +
        "Frontmatter fields (tags, status, type, date, extra fields) are included in the index. " +
        "Requires Ollama running with the configured embedding model.")]
    public async Task<string> search_notes_semantic(
        [Description("Natural language query. E.g. 'notes about stress and burnout'.")] string query,
        [Description("Maximum number of results to return (default: 10).")] int max_results = 10,
        [Description("Minimum similarity score 0.0–1.0 to include a result (default: 0.4). Use 0 to disable filtering or 0.7 to keep only high-confidence matches.")] float min_score = DefaultSemanticMinScore)
    {
        Count(nameof(search_notes_semantic), metrics);
        if (!embedding.IsAvailable)
        {
            return $"[info] Semantic search unavailable — Ollama is not running at {config.OllamaUrl}";
        }

        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return KiokuError.InvalidArgument("The 'query' parameter cannot be empty.");
        }

        var queryVector = await embedding.EmbedQueryAsync(query);
        if (queryVector is null)
        {
            return KiokuError.DependencyUnavailable("Could not generate embedding for query. Is Ollama running?");
        }

        var notesByPath = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);

        var results = embedding
            .SearchByVector(queryVector, Math.Min(max_results, config.MaxSearchResults), string.Empty, notesByPath, min_score)
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

    // search_notes_hybrid

    [McpServerTool, Description(
        "Searches notes combining keyword and semantic search using Reciprocal Rank Fusion (RRF). " +
        "Finds notes that match by exact terms AND by conceptual meaning. " +
        "Best general-purpose search when you are unsure whether to use keyword or semantic search. " +
        "Requires Ollama for the semantic leg — degrades to keyword-only if Ollama is unavailable.")]
    public async Task<string> search_notes_hybrid(
        [Description("Search query in natural language or keywords.")] string query,
        [Description("Maximum number of results to return (default: 10).")] int max_results = 10,
        [Description("Minimum RRF score 0.0–1.0 to include a result (default: 0.0 = no filter).")] float min_score = 0f,
        [Description("Weight for keyword search leg (default: 1.0).")] float keyword_weight = 1f,
        [Description("Weight for semantic search leg (default: 1.0). Set to 0 to disable semantic.")] float semantic_weight = 1f)
    {
        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return KiokuError.InvalidArgument("The 'query' parameter cannot be empty.");
        }

        float[]? queryVector = null;
        if (embedding.IsAvailable && semantic_weight > 0f)
        {
            queryVector = await embedding.EmbedQueryAsync(query);
        }

        var capped = Math.Min(max_results, config.MaxSearchResults);
        var results = hybrid
            .Search(query, capped, min_score, keyword_weight, semantic_weight, queryVector)
            .ToList();

        if (results.Count == 0)
        {
            var threshold = min_score > 0f ? $" above {min_score:P0} score" : "";
            return $"No hybrid results found for: '{query}'{threshold}";
        }

        var semanticAvailable = embedding.IsAvailable && semantic_weight > 0f;
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
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";
            var snippet = r.Snippet is not null ? $"\n   > {r.Snippet}" : "";
            var legs = (r.FromKeyword, r.FromSemantic) switch
            {
                (true, true) => "keyword+semantic",
                (true, false) => "keyword",
                (false, true) => "semantic",
                _ => "hybrid",
            };

            return $"{i + 1}. [{legs}] **{r.Note.Name}**{tags} ({score}% relevance)\n   {r.Note.VaultRelativePath}{snippet}";
        });

        var mode = semanticAvailable ? "keyword + semantic (RRF)" : "keyword only (Ollama unavailable)";
        return $"{results.Count} result(s) for '{query}' [{mode}]:\n\n" + string.Join("\n\n", lines);
    }

    // find_similar_notes

    [McpServerTool, Description(
        "Finds notes conceptually similar to a given note using semantic embeddings. " +
        "Unlike search_notes_semantic (which takes a text query), this tool takes a note name and finds " +
        "notes similar to it — useful for discovering hidden connections in the vault. " +
        "Requires Ollama.")]
    public string find_similar_notes(
        [Description("Name or path of the source note.")] string note,
        [Description("Maximum number of similar notes to return (default: 10).")] int max_results = 10,
        [Description("Minimum similarity score 0.0–1.0 (default: 0.5).")] float min_score = 0.5f)
    {
        if (!embedding.IsAvailable)
        {
            return $"[info] Semantic search unavailable — Ollama is not running at {config.OllamaUrl}";
        }

        if (!vault.IsReady)
        {
            return "[loading] The index is still loading.";
        }

        var source = ResolveNote(note);
        if (source is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'. Use list_notes to see available notes.");
        }

        var capped = Math.Min(max_results, config.MaxSearchResults);
        var results = hybrid.FindSimilar(source, capped, min_score).ToList();

        if (results.Count == 0)
        {
            return $"No notes similar to '{source.Name}' found above {min_score:P0} similarity.";
        }

        var lines = results.Select((r, i) =>
        {
            var score = (r.Score * 100).ToString("F0");
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";
            var snippet = BuildSemanticSnippet(r.Note.PlainText);
            var snippetStr = snippet is not null ? $"\n   > {snippet}" : "";
            return $"{i + 1}. [similar] **{r.Note.Name}**{tags} ({score}% similarity)\n   {r.Note.VaultRelativePath}{snippetStr}";
        });

        return $"{results.Count} note(s) similar to '{source.Name}':\n\n" + string.Join("\n\n", lines);
    }

    // get_note_embedding

    [McpServerTool, Description(
        "Returns diagnostic information about the embedding vector of a note. " +
        "Shows the vector dimensions and a preview of the first values. " +
        "Use to verify that a note has been indexed for semantic search.")]
    public string get_note_embedding(
        [Description("Name or path of the note.")] string note)
    {
        if (!embedding.IsAvailable)
        {
            return $"[info] Embeddings unavailable — Ollama is not running at {config.OllamaUrl}";
        }

        var found = ResolveNote(note);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var vector = hybrid.GetEmbedding(found.VaultRelativePath);
        if (vector is null)
        {
            return $"[info] Note '{found.Name}' has no embedding yet — it may still be indexing.";
        }

        var preview = string.Join(", ", vector.Take(8).Select(v => v.ToString("F4")));
        return $"""
               **{found.Name}** — embedding info

               Dimensions:  {vector.Length}
               Path:        {found.VaultRelativePath}
               Preview:     [{preview}, ...]
               """;
    }

    // Private helpers

    private static string? BuildSemanticSnippet(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        var trimmed = plainText.Trim();
        return trimmed.Length > 200 ? trimmed[..200].Trim() + "\u2026" : trimmed;
    }

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);

    // inspect_note_tags

    [McpServerTool, Description(
        "Returns the current tag state of a note to help an AI agent decide which new tags to add. " +
        "Reports existing tags, folder-inherited tags from config.yml auto_tags, " +
        "and frontmatter fields that must not be duplicated as tags. " +
        "After reading this, the AI agent should call add_tag with any missing semantic tags.")]
    public string inspect_note_tags(
        [Description("Name or vault-relative path of the note.")] string note)
    {
        var found = NoteHelpers.ResolveNote(note, vault);
        if (found is null)
        {
            return KiokuError.NotFound($"Note not found: '{note}'");
        }

        var folder = Path.GetDirectoryName(found.VaultRelativePath)?.Replace('\\', '/') ?? string.Empty;
        var inherited = vaultConfig.GetInheritedTags(folder);
        var excluded = vaultConfig.ExcludeFromTags;
        var existing = found.Metadata.Tags;

        var notYetApplied = inherited
            .Where(t => !existing.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"[ok] Tag state for '{found.Name}':");
        sb.AppendLine($"  Path: {found.VaultRelativePath}");
        sb.AppendLine($"  Folder: {(string.IsNullOrEmpty(folder) ? "(root)" : folder)}");
        sb.AppendLine();
        sb.AppendLine("  Frontmatter (do NOT add as tags):");
        if (!string.IsNullOrWhiteSpace(found.Metadata.NoteType))
        {
            sb.AppendLine($"    type: {found.Metadata.NoteType}");
        }

        if (!string.IsNullOrWhiteSpace(found.Metadata.Status))
        {
            sb.AppendLine($"    status: {found.Metadata.Status}");
        }

        if (!string.IsNullOrWhiteSpace(found.Metadata.Domain))
        {
            sb.AppendLine($"    domain: {found.Metadata.Domain}");
        }

        sb.AppendLine();
        sb.AppendLine(existing.Count > 0
            ? $"  Existing tags ({existing.Count}): {string.Join(", ", existing)}"
            : "  Existing tags: (none)");
        if (notYetApplied.Count > 0)
        {
            sb.AppendLine($"  Inherited from config (not yet applied): {string.Join(", ", notYetApplied)}");
        }

        sb.AppendLine($"  Fields excluded from tagging: {string.Join(", ", excluded)}");
        sb.AppendLine();
        sb.AppendLine("  [instruction] Read the note content and propose additional semantic tags.");
        sb.AppendLine("  [instruction] Call add_tag with only tags NOT already listed above.");

        return sb.ToString().TrimEnd();
    }
}
