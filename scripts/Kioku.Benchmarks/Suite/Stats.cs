namespace Kioku.Benchmarks.Suite;

/// <summary>Small shared statistics helpers used by every latency-sampling benchmark.</summary>
public static class Stats
{
    /// <summary>Linear-interpolation percentile (0.0-1.0) over an already-sorted list.</summary>
    public static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }

        var rank = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper)
        {
            return sortedValues[lower];
        }

        var fraction = rank - lower;
        return sortedValues[lower] + (fraction * (sortedValues[upper] - sortedValues[lower]));
    }
}
