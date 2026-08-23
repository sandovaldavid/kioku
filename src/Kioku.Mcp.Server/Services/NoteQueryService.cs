using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Presentation;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Read-only note-query workflows: reading, listing, searching, following wikilinks, and
/// finding conceptually similar notes. Delegates data access to VaultIndexService/
/// EmbeddingService/HybridSearchService and text/JSON formatting to
/// <see cref="NoteResultPresenter"/> — this type only decides what happened.
/// </summary>
internal sealed class NoteQueryService(
    VaultIndexService vault,
    KiokuConfiguration config,
    EmbeddingService embedding,
    HybridSearchService hybrid,
    MetricsService? metrics = null) : INoteQueryService
{
    // Default similarity floor for semantic results: filters unrelated notes that cosine
    // similarity still scores in the low range. Tune against a golden set with the
    // Kioku.Eval runner (docs/retrieval-eval.md); pass min_score=0 to disable filtering.
    private const float DefaultSemanticMinScore = 0.4f;

    public async Task<string> ReadNoteAsync(
        string note, bool metadataOnly, string format, CancellationToken cancellationToken = default)
    {
        metrics?.RecordToolCall("read_note");
        var found = ResolveNote(note);
        if (found is null)
        {
            return NoteResultPresenter.RenderReadNoteNotFound(note, format);
        }

        if (metadataOnly)
        {
            return NoteResultPresenter.RenderMetadata(found, format);
        }

        // Re-read from disk to have the most up-to-date content. Use the shared-read, retrying
        // helper (not File.ReadAllTextAsync) so a concurrent writer (Obsidian, Git, another
        // agent) holding the file briefly doesn't surface as an IOException sharing violation.
        var content = await NoteHelpers.ReadAllTextAsync(found.FilePath, cancellationToken);
        return NoteResultPresenter.RenderReadNoteContent(found, content, format);
    }

    public string ListNotes(
        string folder,
        string? tag,
        string? status,
        string? type,
        string? dateFrom,
        string? dateTo,
        int limit,
        int offset,
        string format)
    {
        metrics?.RecordToolCall("list_notes");
        if (!vault.IsReady)
        {
            return NoteResultPresenter.RenderListNotesLoading(format);
        }

        if (offset < 0)
        {
            return NoteResultPresenter.RenderInvalidOffset(format);
        }

        if (limit <= 0)
        {
            return NoteResultPresenter.RenderInvalidLimit(format);
        }

        if (!TryParseDate(dateFrom, out var from) || !TryParseDate(dateTo, out var to))
        {
            return NoteResultPresenter.RenderInvalidDateFormat(format);
        }

        if (from.HasValue && to.HasValue && from > to)
        {
            return NoteResultPresenter.RenderInvalidDateRange(format);
        }

        limit = Math.Min(limit, config.MaxSearchResults);

        var notes = string.IsNullOrWhiteSpace(folder)
            ? vault.GetAllNotes()
            : vault.GetNotesInFolder(folder);

        var hasMetadataFilter = tag is not null || status is not null || type is not null ||
                                dateFrom is not null || dateTo is not null;
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

        return page.Count == 0
            ? NoteResultPresenter.RenderEmptyNoteList(format, folder, total, offset, limit)
            : NoteResultPresenter.RenderNoteList(format, folder, total, offset, limit, page);
    }

    public async Task<string> SearchNotesAsync(
        string query,
        string mode,
        int maxResults,
        float minScore,
        string format,
        CancellationToken cancellationToken = default)
    {
        metrics?.RecordToolCall("search_notes");
        if (!vault.IsReady)
        {
            return NoteResultPresenter.RenderSearchNotesLoading(format);
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return NoteResultPresenter.RenderInvalidQuery(format);
        }

        if (maxResults <= 0)
        {
            return NoteResultPresenter.RenderInvalidMaxResults(format);
        }

        if (minScore is < -1f or > 1f)
        {
            return NoteResultPresenter.RenderInvalidMinScore(format);
        }

        var capped = Math.Min(maxResults, config.MaxSearchResults);
        return mode.ToLowerInvariant() switch
        {
            "keyword" => SearchKeyword(query, capped, format),
            "semantic" => await SearchSemanticAsync(
                query, capped, minScore < 0f ? DefaultSemanticMinScore : minScore, format, cancellationToken),
            "hybrid" => await SearchHybridAsync(query, capped, Math.Max(minScore, 0f), format, cancellationToken),
            _ => NoteResultPresenter.RenderUnknownMode(mode, format),
        };
    }

    private string SearchKeyword(string query, int maxResults, string format)
    {
        var results = vault.Search(query, maxResults).ToList();
        if (results.Count == 0)
        {
            return NoteResultPresenter.RenderNoKeywordResults(query, format);
        }

        var rows = results.Select(r => new NoteSearchRow(
            r.Note,
            r.Score,
            NoteResultPresenter.MatchTypeLabel(r.MatchType),
            r.Snippet));
        return NoteResultPresenter.RenderSearchResults(query, "keyword", rows, format);
    }

    private async Task<string> SearchSemanticAsync(
        string query, int maxResults, float minScore, string format, CancellationToken cancellationToken)
    {
        if (!embedding.IsAvailable)
        {
            return NoteResultPresenter.RenderSemanticUnavailable(query, config.OllamaUrl, format);
        }

        var queryVector = await embedding.EmbedQueryAsync(query, cancellationToken);
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
            return NoteResultPresenter.RenderNoSemanticResults(query, minScore, format);
        }

        var rows = results.Select(r => new NoteSearchRow(
            r.Note,
            r.Score,
            "semantic",
            NoteResultPresenter.BuildSemanticSnippet(r.Note.PlainText)));
        return NoteResultPresenter.RenderSearchResults(query, "semantic", rows, format);
    }

    private async Task<string> SearchHybridAsync(
        string query, int maxResults, float minScore, string format, CancellationToken cancellationToken)
    {
        float[]? queryVector = null;
        if (embedding.IsAvailable)
        {
            queryVector = await embedding.EmbedQueryAsync(query, cancellationToken);
        }

        var results = hybrid
            .Search(query, maxResults, minScore, keywordWeight: 1f, semanticWeight: 1f, queryVector)
            .ToList();

        if (results.Count == 0)
        {
            return NoteResultPresenter.RenderNoHybridResults(query, minScore, format);
        }

        var rows = results.Select(r => new NoteSearchRow(
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
        return NoteResultPresenter.RenderSearchResults(query, modeLabel, rows, format);
    }

    public string GetLinks(string note, string direction, string format)
    {
        metrics?.RecordToolCall("get_links");
        if (!vault.IsReady)
        {
            return NoteResultPresenter.RenderGetLinksLoading(format);
        }

        var dir = direction.ToLowerInvariant();
        if (dir is not ("in" or "out" or "both"))
        {
            return NoteResultPresenter.RenderUnknownDirection(direction, format);
        }

        var found = ResolveNote(note);
        if (found is null)
        {
            return NoteResultPresenter.RenderGetLinksNotFound(note, format);
        }

        var name = found.Name;

        List<Note>? backlinks = null;
        List<string>? outgoing = null;
        List<OutgoingLinkResolutionRow>? outgoingResolutions = null;
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
            outgoingResolutions = outgoing
                .Select(link =>
                {
                    var resolution = vault.ResolveLinkResult(found, link);
                    return new OutgoingLinkResolutionRow(
                        link,
                        resolution.Status.ToString().ToLowerInvariant(),
                        resolution.CanonicalTargetPath,
                        resolution.Fragment);
                })
                .ToList();
        }

        return NoteResultPresenter.RenderLinks(
            name,
            found.VaultRelativePath,
            backlinks,
            outgoing,
            outgoingResolutions,
            format);
    }

    public string FindSimilarNotes(string note, int maxResults, float minScore)
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

        var capped = Math.Min(maxResults, config.MaxSearchResults);
        var results = hybrid.FindSimilar(source, capped, minScore).ToList();

        if (results.Count == 0)
        {
            return NoteResultPresenter.RenderNoSimilarNotes(source.Name, minScore);
        }

        var rows = results.Select(r => new NoteSearchRow(
            r.Note,
            r.Score,
            "similar",
            NoteResultPresenter.BuildSemanticSnippet(r.Note.PlainText)));
        return NoteResultPresenter.RenderSimilarNotes(source.Name, rows);
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
