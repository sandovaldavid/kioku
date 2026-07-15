using System.ComponentModel;
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
    MetricsService? metrics = null)
{
    private const string FormatDescription = "'text' (default) or 'json'.";

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
        "Reads an Obsidian note. Accepts note name (without extension), vault-relative path, " +
        "or absolute path. metadata_only=true returns just the YAML frontmatter metadata " +
        "(tags, aliases, status, type, dates, outgoing link count) without the content. " +
        "Use format='json' for a structured response.")]
    public async Task<string> read_note(
        [Description("Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md'")] string note,
        [Description("Return only frontmatter metadata, not the content.")] bool metadata_only = false,
        [Description(FormatDescription)] string format = "text")
    {
        Count(nameof(read_note), metrics);
        var found = ResolveNote(note);
        if (found is null)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Note not found: '{note}'" })
                : KiokuError.NotFound($"Note not found: '{note}'. Use list_notes to see available notes.");
        }

        if (metadata_only)
        {
            return RenderMetadata(found, format);
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

    private static string RenderMetadata(Note found, string format)
    {
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

    // list_notes

    [McpServerTool, Description(
        "Lists notes in the vault or a folder, optionally filtered by frontmatter metadata " +
        "(tag, status, type, date range — combined with AND). Supports pagination via offset " +
        "and limit. Use format='json' for a structured response.")]
    public string list_notes(
        [Description("Folder to list (relative to the vault). Leave empty for the entire vault.")] string folder = "",
        [Description("Filter by tag (e.g. 'project').")] string? tag = null,
        [Description("Filter by frontmatter status (e.g. 'draft').")] string? status = null,
        [Description("Filter by note type (e.g. 'zettel').")] string? type = null,
        [Description("Minimum frontmatter date (YYYY-MM-DD).")] string? date_from = null,
        [Description("Maximum frontmatter date (YYYY-MM-DD).")] string? date_to = null,
        [Description("Maximum notes to return (default: 50, capped by KIOKU_MAX_RESULTS).")] int limit = 50,
        [Description("Number of notes to skip for pagination.")] int offset = 0,
        [Description(FormatDescription)] string format = "text")
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

        if (!TryParseDate(date_from, out var from) || !TryParseDate(date_to, out var to))
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'date_from' and 'date_to' must use YYYY-MM-DD." })
                : KiokuError.InvalidArgument("'date_from' and 'date_to' must use YYYY-MM-DD.");
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'date_from' cannot be later than 'date_to'." })
                : KiokuError.InvalidArgument("'date_from' cannot be later than 'date_to'.");
        }

        limit = Math.Min(limit, config.MaxSearchResults);

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var hasMetadataFilter = tag is not null || status is not null || type is not null ||
                                date_from is not null || date_to is not null;
        if (hasMetadataFilter)
        {
            var matching = vault.FilterByMetadata(tag, status, type, from, to)
                .Select(n => n.VaultRelativePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            notes = notes.Where(n => matching.Contains(n.VaultRelativePath));
        }

        var sorted = notes.OrderBy(n => n.VaultRelativePath).ToList();
        var total = sorted.Count;
        var page = sorted.Skip(offset).Take(limit).ToList();

        if (page.Count == 0)
        {
            var emptyMessage = string.IsNullOrWhiteSpace(folder)
                ? "No notes match (or the requested page is empty)."
                : $"No matching notes in folder '{folder}' (or the requested page is empty).";
            return IsJsonFormat(format)
                ? ToJson(new { total, offset, limit, folder, notes = Array.Empty<object>() })
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
                    status = n.Metadata.Status,
                    type = n.Metadata.NoteType,
                    date = n.Metadata.Date?.ToString("yyyy-MM-dd"),
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

    // Default similarity floor for semantic results: filters unrelated notes that cosine
    // similarity still scores in the low range. Tune against a golden set with the
    // Kioku.Eval runner (docs/retrieval-eval.md); pass min_score=0 to disable filtering.
    private const float DefaultSemanticMinScore = 0.4f;

    [McpServerTool, Description(
        "Searches notes. mode='hybrid' (default) combines keyword and semantic search via " +
        "Reciprocal Rank Fusion and degrades to keyword-only without Ollama; mode='keyword' " +
        "matches title/content/tags exactly; mode='semantic' matches by meaning (requires " +
        "Ollama). Use format='json' for a structured response.")]
    public async Task<string> search_notes(
        [Description("Search query: keywords or natural language.")] string query,
        [Description("'hybrid' (default), 'keyword', or 'semantic'.")] string mode = "hybrid",
        [Description("Maximum number of results (default: 10).")] int max_results = 10,
        [Description("Minimum score 0.0–1.0 to include a result. Default: 0.4 for semantic, no filter otherwise.")] float min_score = -1f,
        [Description(FormatDescription)] string format = "text")
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

        if (max_results <= 0)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'max_results' must be greater than 0." })
                : KiokuError.InvalidArgument("'max_results' must be greater than 0.");
        }

        if (min_score is < -1f or > 1f)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "'min_score' must be between 0 and 1, or -1 to use the mode default." })
                : KiokuError.InvalidArgument("'min_score' must be between 0 and 1, or -1 to use the mode default.");
        }

        var capped = Math.Min(max_results, config.MaxSearchResults);
        return mode.ToLowerInvariant() switch
        {
            "keyword" => SearchKeyword(query, capped, format),
            "semantic" => await SearchSemanticAsync(query, capped, min_score < 0f ? DefaultSemanticMinScore : min_score, format),
            "hybrid" => await SearchHybridAsync(query, capped, Math.Max(min_score, 0f), format),
            _ => IsJsonFormat(format)
                ? ToJson(new { error = $"Unknown mode '{mode}'. Use 'hybrid', 'keyword', or 'semantic'." })
                : KiokuError.InvalidArgument($"Unknown mode '{mode}'. Use 'hybrid', 'keyword', or 'semantic'."),
        };
    }

    private string SearchKeyword(string query, int maxResults, string format)
    {
        var results = vault.Search(query, maxResults).ToList();
        if (results.Count == 0)
        {
            return IsJsonFormat(format)
                ? ToJson(new { query, mode = "keyword", results = Array.Empty<object>() })
                : $"No notes found for: '{query}'";
        }

        var rows = results.Select(r => new SearchRow(
            r.Note,
            r.Score,
            MatchTypeLabel(r.MatchType),
            r.Snippet));
        return RenderSearchResults(query, "keyword", rows, format);
    }

    private async Task<string> SearchSemanticAsync(string query, int maxResults, float minScore, string format)
    {
        if (!embedding.IsAvailable)
        {
            return IsJsonFormat(format)
                ? ToJson(new { query, mode = "semantic", error = $"[info] Semantic search unavailable — Ollama is not running at {config.OllamaUrl}" })
                : $"[info] Semantic search unavailable — Ollama is not running at {config.OllamaUrl}";
        }

        var queryVector = await embedding.EmbedQueryAsync(query);
        if (queryVector is null)
        {
            return KiokuError.DependencyUnavailable("Could not generate embedding for query. Is Ollama running?");
        }

        var notesByPath = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);

        var results = embedding
            .SearchByVector(queryVector, maxResults, string.Empty, notesByPath, minScore)
            .ToList();

        if (results.Count == 0)
        {
            var threshold = minScore > 0f ? $" above {minScore:P0} similarity" : "";
            return IsJsonFormat(format)
                ? ToJson(new { query, mode = "semantic", results = Array.Empty<object>() })
                : $"No semantically similar notes found for: '{query}'{threshold}";
        }

        var rows = results.Select(r => new SearchRow(
            r.Note,
            r.Score,
            "semantic",
            BuildSemanticSnippet(r.Note.PlainText)));
        return RenderSearchResults(query, "semantic", rows, format);
    }

    private async Task<string> SearchHybridAsync(string query, int maxResults, float minScore, string format)
    {
        float[]? queryVector = null;
        if (embedding.IsAvailable)
        {
            queryVector = await embedding.EmbedQueryAsync(query);
        }

        var results = hybrid
            .Search(query, maxResults, minScore, keywordWeight: 1f, semanticWeight: 1f, queryVector)
            .ToList();

        if (results.Count == 0)
        {
            var threshold = minScore > 0f ? $" above {minScore:P0} score" : "";
            return IsJsonFormat(format)
                ? ToJson(new { query, mode = "hybrid", results = Array.Empty<object>() })
                : $"No hybrid results found for: '{query}'{threshold}";
        }

        var rows = results.Select(r => new SearchRow(
            r.Note,
            r.Score,
            (r.FromKeyword, r.FromSemantic) switch
            {
                (true, true) => "keyword+semantic",
                (true, false) => "keyword",
                (false, true) => "semantic",
                _ => "hybrid",
            },
            r.Snippet));
        var modeLabel = embedding.IsAvailable ? "hybrid" : "hybrid (keyword only — Ollama unavailable)";
        return RenderSearchResults(query, modeLabel, rows, format);
    }

    private sealed record SearchRow(Note Note, float Score, string Label, string? Snippet);

    private static string MatchTypeLabel(NoteMatchType matchType) => matchType switch
    {
        NoteMatchType.TitleMatch => "title",
        NoteMatchType.TagMatch => "tag",
        NoteMatchType.ContentMatch => "content",
        _ => "match",
    };

    private static string RenderSearchResults(string query, string mode, IEnumerable<SearchRow> rows, string format)
    {
        var list = rows.ToList();
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                query,
                mode,
                results = list.Select((r, i) => new
                {
                    rank = i + 1,
                    name = r.Note.Name,
                    path = r.Note.VaultRelativePath,
                    score = r.Score,
                    match = r.Label,
                    snippet = r.Snippet,
                    tags = r.Note.Metadata.Tags,
                }),
            });
        }

        var lines = list.Select((r, i) =>
        {
            var score = (r.Score * 100).ToString("F0");
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";
            var snippet = r.Snippet is not null ? $"\n   > {r.Snippet}" : "";
            return $"{i + 1}. [{r.Label}] **{r.Note.Name}**{tags} ({score}% relevance)\n   {r.Note.VaultRelativePath}{snippet}";
        });

        return $"{list.Count} result(s) for '{query}' [{mode}]:\n\n" + string.Join("\n\n", lines);
    }

    // get_links

    [McpServerTool, Description(
        "Returns the wikilink connections of a note. direction='in' lists notes linking TO it " +
        "(backlinks), 'out' lists wikilinks FROM it, 'both' (default) lists both. " +
        "Use format='json' for a structured response.")]
    public string get_links(
        [Description("Name or path of the note.")] string note,
        [Description("'both' (default), 'in' (backlinks), or 'out' (outgoing).")] string direction = "both",
        [Description(FormatDescription)] string format = "text")
    {
        Count(nameof(get_links), metrics);
        if (!vault.IsReady)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = "[loading] The index is still loading." })
                : "[loading] The index is still loading.";
        }

        var dir = direction.ToLowerInvariant();
        if (dir is not ("in" or "out" or "both"))
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Unknown direction '{direction}'. Use 'in', 'out', or 'both'." })
                : KiokuError.InvalidArgument($"Unknown direction '{direction}'. Use 'in', 'out', or 'both'.");
        }

        var found = ResolveNote(note);
        if (found is null)
        {
            return IsJsonFormat(format)
                ? ToJson(new { error = $"Note not found: '{note}'" })
                : KiokuError.NotFound($"Note not found: '{note}'");
        }

        var name = found.Name;

        List<Note>? backlinks = null;
        List<string>? outgoing = null;
        if (dir is "in" or "both")
        {
            var backlinkCandidates = vault.GetBacklinks(name)
                .Concat(vault.GetBacklinks(StripMdExtension(found.VaultRelativePath)))
                .GroupBy(n => n.FilePath, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());
            backlinks = backlinkCandidates.OrderBy(n => n.Name).ToList();
        }

        if (dir is "out" or "both")
        {
            outgoing = found.OutgoingLinks.OrderBy(l => l).ToList();
        }

        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                note = name,
                path = found?.VaultRelativePath,
                backlinks = backlinks?.Select(n => new { name = n.Name, path = n.VaultRelativePath }),
                outgoing_links = outgoing,
            });
        }

        var sections = new List<string>();
        if (backlinks is not null)
        {
            sections.Add(backlinks.Count == 0
                ? $"No notes link to '{name}'."
                : $"{backlinks.Count} note(s) link to '[[{name}]]':\n" +
                  string.Join("\n", backlinks.Select(n => $"- [[{n.Name}]] → {n.VaultRelativePath}")));
        }

        if (outgoing is not null)
        {
            sections.Add(outgoing.Count == 0
                ? $"The note '{name}' does not contain outgoing links."
                : $"{outgoing.Count} outgoing link(s) in '{name}':\n" +
                  string.Join("\n", outgoing.Select(l => $"- [[{l}]]")));
        }

        return string.Join("\n\n", sections);
    }

    // find_similar_notes

    [McpServerTool, Description(
        "Finds notes conceptually similar to a given note using semantic embeddings. " +
        "Unlike search_notes (which takes a text query), this takes a note and finds notes " +
        "similar to it — useful for discovering hidden connections. Requires Ollama.")]
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

    // Private helpers

    private static string? BuildSemanticSnippet(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        var trimmed = plainText.Trim();
        return trimmed.Length > 200 ? trimmed[..200].Trim() + "…" : trimmed;
    }

    private Note? ResolveNote(string nameOrPath) => NoteHelpers.ResolveNote(nameOrPath, vault);

    private static bool TryParseDate(string? value, out DateOnly? date)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            date = null;
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", out var parsed))
        {
            date = parsed;
            return true;
        }

        date = null;
        return false;
    }

    private static string StripMdExtension(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? path[..^3] : path;
}
