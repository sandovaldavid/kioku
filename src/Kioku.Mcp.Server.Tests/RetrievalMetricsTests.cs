using Kioku.Mcp.Server.Domain;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Hand-computed expected values for the retrieval-quality metrics. The evaluator must be
/// trustworthy on its own before it is used to judge search configurations.
/// </summary>
public class RetrievalMetricsTests
{
    private static Dictionary<string, int> Judgments(params (string Path, int Grade)[] entries) =>
        entries.ToDictionary(e => e.Path, e => e.Grade);

    [Fact]
    public void PrecisionAtK_TwoRelevantInTopFive_Returns0Point4()
    {
        var ranked = new[] { "A.md", "B.md", "C.md", "D.md", "E.md" };
        var judgments = Judgments(("A.md", 1), ("C.md", 2));

        Assert.Equal(0.4, RetrievalMetrics.PrecisionAtK(ranked, judgments, 5), precision: 10);
    }

    [Fact]
    public void PrecisionAtK_FewerResultsThanK_PenalizesMissingResults()
    {
        // 1 relevant hit but only 2 results returned for k=5: precision is 1/5, not 1/2.
        var ranked = new[] { "A.md", "B.md" };
        var judgments = Judgments(("A.md", 1));

        Assert.Equal(0.2, RetrievalMetrics.PrecisionAtK(ranked, judgments, 5), precision: 10);
    }

    [Fact]
    public void RecallAtK_FindsOneOfTwoRelevant_ReturnsHalf()
    {
        var ranked = new[] { "A.md", "B.md", "C.md" };
        var judgments = Judgments(("A.md", 3), ("Z.md", 1));

        Assert.Equal(0.5, RetrievalMetrics.RecallAtK(ranked, judgments, 3), precision: 10);
    }

    [Fact]
    public void RecallAtK_AllRelevantWithinK_ReturnsOne()
    {
        var ranked = new[] { "B.md", "A.md", "C.md" };
        var judgments = Judgments(("A.md", 1), ("B.md", 2));

        Assert.Equal(1.0, RetrievalMetrics.RecallAtK(ranked, judgments, 10), precision: 10);
    }

    [Fact]
    public void RecallAtK_DuplicatePathInRanking_CountsOnce()
    {
        var ranked = new[] { "A.md", "A.md", "B.md" };
        var judgments = Judgments(("A.md", 1), ("Z.md", 1));

        Assert.Equal(0.5, RetrievalMetrics.RecallAtK(ranked, judgments, 3), precision: 10);
    }

    [Fact]
    public void ReciprocalRank_FirstRelevantAtRankThree_ReturnsOneThird()
    {
        var ranked = new[] { "X.md", "Y.md", "A.md" };
        var judgments = Judgments(("A.md", 2));

        Assert.Equal(1.0 / 3.0, RetrievalMetrics.ReciprocalRank(ranked, judgments), precision: 10);
    }

    [Fact]
    public void ReciprocalRank_NoRelevantInRanking_ReturnsZero()
    {
        var ranked = new[] { "X.md", "Y.md" };
        var judgments = Judgments(("A.md", 1));

        Assert.Equal(0.0, RetrievalMetrics.ReciprocalRank(ranked, judgments), precision: 10);
    }

    [Fact]
    public void NdcgAtK_HandComputedExample_MatchesExpectedValue()
    {
        // Ranking: A(grade 3), B(not relevant), C(grade 2), k=3.
        // DCG  = (2^3-1)/log2(2) + 0 + (2^2-1)/log2(4) = 7.0 + 1.5 = 8.5
        // IDCG = 7/log2(2) + 3/log2(3) = 7.0 + 1.8927892607 = 8.8927892607
        // NDCG = 8.5 / 8.8927892607 = 0.9558315...
        var ranked = new[] { "A.md", "B.md", "C.md" };
        var judgments = Judgments(("A.md", 3), ("C.md", 2));

        Assert.Equal(0.95583, RetrievalMetrics.NdcgAtK(ranked, judgments, 3), precision: 5);
    }

    [Fact]
    public void NdcgAtK_IdealOrdering_ReturnsOne()
    {
        var ranked = new[] { "A.md", "C.md", "B.md" };
        var judgments = Judgments(("A.md", 3), ("C.md", 2));

        Assert.Equal(1.0, RetrievalMetrics.NdcgAtK(ranked, judgments, 3), precision: 10);
    }

    [Fact]
    public void NdcgAtK_RelevantMissingFromRanking_IdcgStillCountsIt()
    {
        // Only C (grade 2) retrieved at rank 1; A (grade 3) missing entirely.
        // DCG = 3/log2(2) = 3.0; IDCG = 7 + 3/log2(3) = 8.8927892607
        var ranked = new[] { "C.md", "X.md" };
        var judgments = Judgments(("A.md", 3), ("C.md", 2));

        Assert.Equal(3.0 / 8.8927892607, RetrievalMetrics.NdcgAtK(ranked, judgments, 5), precision: 5);
    }

    [Fact]
    public void Metrics_EmptyRelevanceSet_ReturnZero()
    {
        var ranked = new[] { "A.md" };
        var judgments = new Dictionary<string, int>();

        Assert.Equal(0.0, RetrievalMetrics.RecallAtK(ranked, judgments, 5), precision: 10);
        Assert.Equal(0.0, RetrievalMetrics.ReciprocalRank(ranked, judgments), precision: 10);
        Assert.Equal(0.0, RetrievalMetrics.NdcgAtK(ranked, judgments, 5), precision: 10);
    }

    [Fact]
    public void Metrics_PathsWithBackslashesAndDifferentCase_StillMatch()
    {
        var ranked = new[] { @"Salud\Burnout.md" };
        var judgments = Judgments(("salud/burnout.md", 2));

        Assert.Equal(1.0, RetrievalMetrics.RecallAtK(ranked, judgments, 1), precision: 10);
        Assert.Equal(1.0, RetrievalMetrics.ReciprocalRank(ranked, judgments), precision: 10);
    }

    [Fact]
    public void Metrics_ZeroOrNegativeGrades_AreTreatedAsNotRelevant()
    {
        var ranked = new[] { "A.md", "B.md" };
        var judgments = Judgments(("A.md", 0), ("B.md", 2));

        Assert.Equal(0.5, RetrievalMetrics.PrecisionAtK(ranked, judgments, 2), precision: 10);
        Assert.Equal(0.5, RetrievalMetrics.ReciprocalRank(ranked, judgments), precision: 10);
    }
}
