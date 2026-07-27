using System.ComponentModel;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP query tools (read-only) for the Obsidian vault.
/// All operations here are read-only — they do not modify files.
/// </summary>
[McpServerToolType]
public sealed class NoteQueryTools
{
    private const string FormatDescription = "'text' (default) or 'json'.";

    private readonly INoteQueryService _queries;

    public NoteQueryTools(INoteQueryService queries)
    {
        _queries = queries;
    }

    [McpServerTool, Description(
        "Reads an Obsidian note. Accepts note name (without extension), vault-relative path, " +
        "or absolute path. metadata_only=true returns just the YAML frontmatter metadata " +
        "(tags, aliases, status, type, dates, outgoing link count) without the content. " +
        "Use format='json' for a structured response.")]
    public Task<string> read_note(
        [Description("Name or path of the note. E.g. 'My Note', 'Projects/Kioku', '/home/user/vault/note.md'")] string note,
        [Description("Return only frontmatter metadata, not the content.")] bool metadata_only = false,
        [Description(FormatDescription)] string format = "text",
        CancellationToken cancellationToken = default) =>
        _queries.ReadNoteAsync(note, metadata_only, format, cancellationToken);

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
        [Description(FormatDescription)] string format = "text") =>
        _queries.ListNotes(folder, tag, status, type, date_from, date_to, limit, offset, format);

    [McpServerTool, Description(
        "Searches notes. mode='hybrid' (default) combines keyword and semantic search via " +
        "Reciprocal Rank Fusion and degrades to keyword-only without Ollama; mode='keyword' " +
        "matches title/content/tags exactly; mode='semantic' matches by meaning (requires " +
        "Ollama). Use format='json' for a structured response.")]
    public Task<string> search_notes(
        [Description("Search query: keywords or natural language.")] string query,
        [Description("'hybrid' (default), 'keyword', or 'semantic'.")] string mode = "hybrid",
        [Description("Maximum number of results (default: 10).")] int max_results = 10,
        [Description("Minimum score 0.0–1.0 to include a result. Default: 0.4 for semantic, no filter otherwise.")] float min_score = -1f,
        [Description(FormatDescription)] string format = "text",
        CancellationToken cancellationToken = default) =>
        _queries.SearchNotesAsync(query, mode, max_results, min_score, format, cancellationToken);

    [McpServerTool, Description(
        "Returns the wikilink connections of a note. direction='in' lists notes linking TO it " +
        "(backlinks), 'out' lists wikilinks FROM it, 'both' (default) lists both. " +
        "Use format='json' for a structured response.")]
    public string get_links(
        [Description("Name or path of the note.")] string note,
        [Description("'both' (default), 'in' (backlinks), or 'out' (outgoing).")] string direction = "both",
        [Description(FormatDescription)] string format = "text") =>
        _queries.GetLinks(note, direction, format);

    [McpServerTool, Description(
        "Finds notes conceptually similar to a given note using semantic embeddings. " +
        "Unlike search_notes (which takes a text query), this takes a note and finds notes " +
        "similar to it — useful for discovering hidden connections. Requires Ollama.")]
    public string find_similar_notes(
        [Description("Name or path of the source note.")] string note,
        [Description("Maximum number of similar notes to return (default: 10).")] int max_results = 10,
        [Description("Minimum similarity score 0.0–1.0 (default: 0.5).")] float min_score = 0.5f) =>
        _queries.FindSimilarNotes(note, max_results, min_score);
}
