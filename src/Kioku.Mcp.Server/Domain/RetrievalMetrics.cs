namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Standard information-retrieval quality metrics computed over a single ranked result list
/// against graded relevance judgments (grade &gt; 0 = relevant, higher = more relevant).
/// Paths are compared case-insensitively with directory separators normalized to '/'.
///
/// For queries with an empty relevance set (deliberate "no relevant answer" probes),
/// RecallAtK, ReciprocalRank and NdcgAtK return 0 — exclude such queries from those
/// aggregates and evaluate them with PrecisionAtK (expected 0 hits) instead.
/// </summary>
public static class RetrievalMetrics
{
    /// <summary>Fraction of the top-k results that are relevant.</summary>
    public static double PrecisionAtK(
        IReadOnlyList<string> rankedPaths,
        IReadOnlyDictionary<string, int> relevanceByPath,
        int k)
    {
        if (k <= 0 || rankedPaths.Count == 0)
        {
            return 0.0;
        }

        var relevant = NormalizeJudgments(relevanceByPath);
        var top = Math.Min(k, rankedPaths.Count);
        var hits = 0;
        for (int i = 0; i < top; i++)
        {
            if (IsRelevant(rankedPaths[i], relevant))
            {
                hits++;
            }
        }

        return (double)hits / k;
    }

    /// <summary>Fraction of all relevant notes that appear in the top-k results.</summary>
    public static double RecallAtK(
        IReadOnlyList<string> rankedPaths,
        IReadOnlyDictionary<string, int> relevanceByPath,
        int k)
    {
        var relevant = NormalizeJudgments(relevanceByPath);
        if (relevant.Count == 0 || k <= 0)
        {
            return 0.0;
        }

        var top = Math.Min(k, rankedPaths.Count);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < top; i++)
        {
            var normalized = NormalizePath(rankedPaths[i]);
            if (relevant.ContainsKey(normalized))
            {
                found.Add(normalized);
            }
        }

        return (double)found.Count / relevant.Count;
    }

    /// <summary>Reciprocal of the rank (1-based) of the first relevant result, or 0 if none appears.</summary>
    public static double ReciprocalRank(
        IReadOnlyList<string> rankedPaths,
        IReadOnlyDictionary<string, int> relevanceByPath)
    {
        var relevant = NormalizeJudgments(relevanceByPath);
        if (relevant.Count == 0)
        {
            return 0.0;
        }

        for (int i = 0; i < rankedPaths.Count; i++)
        {
            if (IsRelevant(rankedPaths[i], relevant))
            {
                return 1.0 / (i + 1);
            }
        }

        return 0.0;
    }

    /// <summary>
    /// Normalized Discounted Cumulative Gain at k with exponential gain (2^grade - 1)
    /// and log2(rank + 1) discount. 1.0 = ideal ordering.
    /// </summary>
    public static double NdcgAtK(
        IReadOnlyList<string> rankedPaths,
        IReadOnlyDictionary<string, int> relevanceByPath,
        int k)
    {
        var relevant = NormalizeJudgments(relevanceByPath);
        if (relevant.Count == 0 || k <= 0)
        {
            return 0.0;
        }

        var dcg = 0.0;
        var top = Math.Min(k, rankedPaths.Count);
        for (int i = 0; i < top; i++)
        {
            if (relevant.TryGetValue(NormalizePath(rankedPaths[i]), out var grade) && grade > 0)
            {
                dcg += Gain(grade) / Math.Log2(i + 2);
            }
        }

        var idealGrades = relevant.Values
            .Where(g => g > 0)
            .OrderByDescending(g => g)
            .Take(k)
            .ToList();

        var idcg = 0.0;
        for (int i = 0; i < idealGrades.Count; i++)
        {
            idcg += Gain(idealGrades[i]) / Math.Log2(i + 2);
        }

        return idcg > 0 ? dcg / idcg : 0.0;
    }

    private static double Gain(int grade) => Math.Pow(2, grade) - 1;

    private static bool IsRelevant(string path, Dictionary<string, int> normalizedJudgments) =>
        normalizedJudgments.TryGetValue(NormalizePath(path), out var grade) && grade > 0;

    private static Dictionary<string, int> NormalizeJudgments(IReadOnlyDictionary<string, int> relevanceByPath)
    {
        var normalized = new Dictionary<string, int>(relevanceByPath.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (path, grade) in relevanceByPath)
        {
            if (grade > 0)
            {
                normalized[NormalizePath(path)] = grade;
            }
        }

        return normalized;
    }

    private static string NormalizePath(string path) => path.Replace('\\', '/').Trim().TrimStart('/');
}
