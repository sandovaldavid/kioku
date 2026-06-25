using Kioku.Mcp.Server.Domain;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Combines keyword search (VaultIndexService) and semantic search (EmbeddingService)
/// using Reciprocal Rank Fusion (RRF) to produce a unified ranked result list.
///
/// RRF formula: score(d) = Σ 1 / (k + rank_i)   where k=60 (standard constant)
///
/// Advantages over single-mode search:
/// - Keyword search catches exact terms that semantics might miss.
/// - Semantic search catches conceptual matches that exact terms miss.
/// - RRF fusion is parameter-free and robust to different score scales.
/// </summary>
public sealed class HybridSearchService(VaultIndexService vault, EmbeddingService embedding)
{
    private const int RrfK = 60;

    /// <summary>
    /// Searches the vault combining keyword and semantic results via RRF.
    /// </summary>
    /// <param name="query">Natural language or keyword query.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="minScore">Minimum RRF score to include a result (0.0 = no filter).</param>
    /// <param name="keywordWeight">Relative weight for keyword results in RRF (default: 1.0).</param>
    /// <param name="semanticWeight">Relative weight for semantic results in RRF (default: 1.0).</param>
    /// <param name="queryVector">Pre-computed query embedding (pass null to skip semantic leg).</param>
    /// <returns>Fused and ranked hybrid results.</returns>
    public IEnumerable<HybridResult> Search(
        string query,
        int maxResults,
        float minScore = 0f,
        float keywordWeight = 1f,
        float semanticWeight = 1f,
        float[]? queryVector = null)
    {
        var cap = Math.Min(maxResults * 5, 200);

        // Keyword leg: retrieve up to 5× maxResults as candidates for fusion
        var keywordResults = vault.Search(query, cap).ToList();

        // Semantic leg: retrieve same pool if embeddings are available
        var notesByPathForSemantic = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);
        var semanticResults = queryVector is not null && embedding.IsAvailable
            ? embedding.SearchByVector(queryVector, cap, string.Empty, notesByPathForSemantic)
            : Enumerable.Empty<SemanticResult>();

        // Build RRF score map: absolute file path → accumulated score
        var rrfScores = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        // Accumulate keyword ranks
        for (int i = 0; i < keywordResults.Count; i++)
        {
            var path = keywordResults[i].Note.FilePath;
            var contribution = keywordWeight / (RrfK + i + 1);
            rrfScores[path] = rrfScores.GetValueOrDefault(path) + contribution;
        }

        // Accumulate semantic ranks
        var semanticList = semanticResults.ToList();
        for (int i = 0; i < semanticList.Count; i++)
        {
            var path = semanticList[i].Note.FilePath;
            var contribution = semanticWeight / (RrfK + i + 1);
            rrfScores[path] = rrfScores.GetValueOrDefault(path) + contribution;
        }

        // Normalize scores to [0, 1] relative to the theoretical maximum
        float maxPossible = (keywordWeight + semanticWeight) / (RrfK + 1);

        // Build a lookup for keyword snippets and match types
        var keywordByPath = keywordResults
            .GroupBy(r => r.Note.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var notesByPath = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);

        return rrfScores
            .Where(kv => notesByPath.ContainsKey(kv.Key))
            .Select(kv =>
            {
                var normalizedScore = maxPossible > 0f ? kv.Value / maxPossible : 0f;
                var note = notesByPath[kv.Key];

                var fromKeyword = keywordByPath.TryGetValue(kv.Key, out var kw);
                var snippet = fromKeyword ? kw!.Snippet : null;
                var matchType = fromKeyword ? kw!.MatchType : NoteMatchType.ContentMatch;
                var inSemantic = semanticList.Any(s => s.Note.FilePath.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));

                return new HybridResult(note, normalizedScore, matchType, snippet, fromKeyword, inSemantic);
            })
            .Where(r => r.Score >= minScore)
            .OrderByDescending(r => r.Score)
            .Take(maxResults);
    }

    /// <summary>
    /// Returns the top-K notes most semantically similar to a given note (not a query).
    /// Used by find_similar_notes.
    /// </summary>
    public IEnumerable<HybridResult> FindSimilar(Note sourceNote, int maxResults, float minScore = 0f)
    {
        if (!embedding.IsAvailable)
        {
            return [];
        }

        var sourceVector = embedding.GetVector(sourceNote.VaultRelativePath);
        if (sourceVector is null)
        {
            return [];
        }

        var notesByPath = vault.GetAllNotes()
            .ToDictionary(n => n.FilePath, StringComparer.OrdinalIgnoreCase);

        return embedding
            .SearchByVector(sourceVector, maxResults + 1, sourceNote.VaultRelativePath, notesByPath, minScore)
            .Where(r => !r.Note.FilePath.Equals(sourceNote.FilePath, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .Select(r => new HybridResult(r.Note, r.Score, NoteMatchType.ContentMatch, null, false, true));
    }

    /// <summary>
    /// Returns the raw embedding vector for a note, for diagnostics.
    /// </summary>
    public float[]? GetEmbedding(string vaultRelativePath) =>
        embedding.GetVector(vaultRelativePath);

}

/// <summary>
/// Result of a hybrid (keyword + semantic) search.
/// </summary>
public sealed class HybridResult
{
    public Note Note { get; }

    /// <summary>Normalized RRF score [0.0 – 1.0]. Higher = more relevant.</summary>
    public float Score { get; }

    /// <summary>Primary match type from the keyword leg (if available).</summary>
    public NoteMatchType MatchType { get; }

    /// <summary>Snippet from the keyword match (if available).</summary>
    public string? Snippet { get; }

    /// <summary>Whether this result was found by the keyword search leg.</summary>
    public bool FromKeyword { get; }

    /// <summary>Whether this result was found by the semantic search leg.</summary>
    public bool FromSemantic { get; }

    public HybridResult(Note note, float score, NoteMatchType matchType, string? snippet, bool fromKeyword, bool fromSemantic)
    {
        Note = note;
        Score = score;
        MatchType = matchType;
        Snippet = snippet;
        FromKeyword = fromKeyword;
        FromSemantic = fromSemantic;
    }
}
