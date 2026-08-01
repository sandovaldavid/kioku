namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for read-only note-query workflows: reading a note, listing notes,
/// searching, following wikilinks, and finding conceptually similar notes. MCP adapters depend
/// on this contract instead of the underlying VaultIndexService/EmbeddingService/
/// HybridSearchService collaborators or building result text themselves.
/// </summary>
public interface INoteQueryService
{
    Task<string> ReadNoteAsync(
        string note,
        bool metadataOnly,
        string format,
        CancellationToken cancellationToken = default);

    string ListNotes(
        string folder,
        string? tag,
        string? status,
        string? type,
        string? dateFrom,
        string? dateTo,
        int limit,
        int offset,
        string format);

    Task<string> SearchNotesAsync(
        string query,
        string mode,
        int maxResults,
        float minScore,
        string format,
        CancellationToken cancellationToken = default);

    string GetLinks(
        string note,
        string direction,
        string format);

    string FindSimilarNotes(
        string note,
        int maxResults,
        float minScore);
}
