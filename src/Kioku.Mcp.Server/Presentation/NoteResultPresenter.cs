using System.Text.Json;
using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Presentation;

/// <summary>
/// Ranked search/similarity row ready for rendering: a note plus the display fields
/// (relevance score, match-type label, optional preview snippet) computed for it.
/// Built by NoteQueryService, rendered by <see cref="NoteResultPresenter"/>.
/// </summary>
internal sealed record NoteSearchRow(Note Note, float Score, string Label, string? Snippet);

/// <summary>
/// Renders NoteQueryService's outcomes as the exact text or JSON strings the note-query MCP
/// tools (search_notes, read_note, list_notes, get_links, find_similar_notes) return. Holds
/// every format decision for that slice: NoteQueryService decides what happened, this type
/// decides how it looks on the wire. No query logic lives here.
/// </summary>
internal static class NoteResultPresenter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    internal static bool IsJsonFormat(string format) =>
        string.Equals(format, "json", StringComparison.OrdinalIgnoreCase);

    private static string ToJson<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    /// <summary>
    /// Shared shape for validation/lookup failures whose JSON body is a bare {error: message}
    /// object. textMessage and jsonMessage are supplied separately because several call sites
    /// use different wording (or a KiokuError code prefix) per format.
    /// </summary>
    internal static string RenderError(string textMessage, string jsonMessage, string format) =>
        IsJsonFormat(format) ? ToJson(new { error = jsonMessage }) : textMessage;

    // read_note

    internal static string RenderReadNoteNotFound(string note, string format) =>
        RenderError(
            KiokuError.NotFound($"Note not found: '{note}'. Use list_notes to see available notes."),
            $"Note not found: '{note}'",
            format);

    internal static string RenderReadNoteContent(Note found, string content, string format) =>
        IsJsonFormat(format)
            ? ToJson(new
            {
                name = found.Name,
                path = found.VaultRelativePath,
                content,
                revision = VaultRevision.Compute(content),
            })
            : content;

    internal static string RenderMetadata(Note found, string format)
    {
        var m = found.Metadata;
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                name = found.Name,
                path = found.VaultRelativePath,
                revision = found.Revision,
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

    internal static string RenderListNotesLoading(string format) =>
        RenderError(
            "[loading] The index is still loading. Wait a moment and try again.",
            "[loading] The index is still loading.",
            format);

    internal static string RenderInvalidOffset(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'offset' must be 0 or greater."),
            "'offset' must be 0 or greater.",
            format);

    internal static string RenderInvalidLimit(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'limit' must be greater than 0."),
            "'limit' must be greater than 0.",
            format);

    internal static string RenderInvalidDateFormat(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'date_from' and 'date_to' must use YYYY-MM-DD."),
            "'date_from' and 'date_to' must use YYYY-MM-DD.",
            format);

    internal static string RenderInvalidDateRange(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'date_from' cannot be later than 'date_to'."),
            "'date_from' cannot be later than 'date_to'.",
            format);

    internal static string RenderEmptyNoteList(string format, string folder, int total, int offset, int limit)
    {
        if (IsJsonFormat(format))
        {
            return ToJson(new { total, offset, limit, folder, notes = Array.Empty<object>() });
        }

        return string.IsNullOrWhiteSpace(folder)
            ? "No notes match (or the requested page is empty)."
            : $"No matching notes in folder '{folder}' (or the requested page is empty).";
    }

    internal static string RenderNoteList(
        string format, string folder, int total, int offset, int limit, IReadOnlyList<Note> page)
    {
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
                    revision = n.Revision,
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

    internal static string RenderSearchNotesLoading(string format) =>
        RenderError(
            "[loading] The index is still loading.",
            "[loading] The index is still loading.",
            format);

    internal static string RenderInvalidQuery(string format) =>
        RenderError(
            KiokuError.InvalidArgument("The 'query' parameter cannot be empty."),
            "The 'query' parameter cannot be empty.",
            format);

    internal static string RenderInvalidMaxResults(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'max_results' must be greater than 0."),
            "'max_results' must be greater than 0.",
            format);

    internal static string RenderInvalidMinScore(string format) =>
        RenderError(
            KiokuError.InvalidArgument("'min_score' must be between 0 and 1, or -1 to use the mode default."),
            "'min_score' must be between 0 and 1, or -1 to use the mode default.",
            format);

    internal static string RenderUnknownMode(string mode, string format) =>
        RenderError(
            KiokuError.InvalidArgument($"Unknown mode '{mode}'. Use 'hybrid', 'keyword', or 'semantic'."),
            $"Unknown mode '{mode}'. Use 'hybrid', 'keyword', or 'semantic'.",
            format);

    internal static string RenderNoKeywordResults(string query, string format) =>
        IsJsonFormat(format)
            ? ToJson(new { query, mode = "keyword", results = Array.Empty<object>() })
            : $"No notes found for: '{query}'";

    internal static string RenderSemanticUnavailable(string query, string ollamaUrl, string format)
    {
        var message = $"[info] Semantic search unavailable — Ollama is not running at {ollamaUrl}";
        return IsJsonFormat(format)
            ? ToJson(new { query, mode = "semantic", error = message })
            : message;
    }

    internal static string RenderNoSemanticResults(string query, float minScore, string format)
    {
        if (IsJsonFormat(format))
        {
            return ToJson(new { query, mode = "semantic", results = Array.Empty<object>() });
        }

        var threshold = minScore > 0f ? $" above {(int)MathF.Round(minScore * 100)}% similarity" : "";
        return $"No semantically similar notes found for: '{query}'{threshold}";
    }

    internal static string RenderNoHybridResults(string query, float minScore, string format)
    {
        if (IsJsonFormat(format))
        {
            return ToJson(new { query, mode = "hybrid", results = Array.Empty<object>() });
        }

        var threshold = minScore > 0f ? $" above {(int)MathF.Round(minScore * 100)}% score" : "";
        return $"No hybrid results found for: '{query}'{threshold}";
    }

    internal static string RenderSearchResults(string query, string mode, IEnumerable<NoteSearchRow> rows, string format)
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
                    revision = r.Note.Revision,
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

    internal static string MatchTypeLabel(NoteMatchType matchType) => matchType switch
    {
        NoteMatchType.TitleMatch => "title",
        NoteMatchType.TagMatch => "tag",
        NoteMatchType.ContentMatch => "content",
        _ => "match",
    };

    internal static string? BuildSemanticSnippet(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return null;
        }

        var trimmed = plainText.Trim();
        return trimmed.Length > 200 ? trimmed[..200].Trim() + "…" : trimmed;
    }

    // get_links

    internal static string RenderGetLinksLoading(string format) =>
        RenderError(
            "[loading] The index is still loading.",
            "[loading] The index is still loading.",
            format);

    internal static string RenderUnknownDirection(string direction, string format) =>
        RenderError(
            KiokuError.InvalidArgument($"Unknown direction '{direction}'. Use 'in', 'out', or 'both'."),
            $"Unknown direction '{direction}'. Use 'in', 'out', or 'both'.",
            format);

    internal static string RenderGetLinksNotFound(string note, string format) =>
        RenderError(
            KiokuError.NotFound($"Note not found: '{note}'"),
            $"Note not found: '{note}'",
            format);

    internal static string RenderLinks(
        string name, string? path, List<Note>? backlinks, List<string>? outgoing, string format)
    {
        if (IsJsonFormat(format))
        {
            return ToJson(new
            {
                note = name,
                path,
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

    internal static string RenderNoSimilarNotes(string sourceName, float minScore) =>
        $"No notes similar to '{sourceName}' found above {(int)MathF.Round(minScore * 100)}% similarity.";

    internal static string RenderSimilarNotes(string sourceName, IEnumerable<NoteSearchRow> rows)
    {
        var list = rows.ToList();
        var lines = list.Select((r, i) =>
        {
            var score = (r.Score * 100).ToString("F0");
            var tags = r.Note.Metadata.Tags.Count > 0
                ? $" [#{string.Join(", #", r.Note.Metadata.Tags)}]"
                : "";
            var snippetStr = r.Snippet is not null ? $"\n   > {r.Snippet}" : "";
            return $"{i + 1}. [similar] **{r.Note.Name}**{tags} ({score}% similarity)\n   {r.Note.VaultRelativePath}{snippetStr}";
        });

        return $"{list.Count} note(s) similar to '{sourceName}':\n\n" + string.Join("\n\n", lines);
    }
}
