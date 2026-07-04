using System.Collections.Concurrent;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Lightweight, opt-in, in-memory metrics collector for Kioku tools.
/// Disabled by default; enable with KIOKU_ENABLE_METRICS=true.
/// Tracks tool call counts only — never note contents or vault data.
/// </summary>
public sealed class MetricsService(KiokuConfiguration config)
{
    private readonly ConcurrentDictionary<string, long> _counters = new();

    public bool Enabled => config.EnableMetrics;

    /// <summary>Records a single invocation of a tool.</summary>
    public void RecordToolCall(string toolName)
    {
        if (!Enabled)
        {
            return;
        }

        _counters.AddOrUpdate(toolName, 1, (_, count) => count + 1);
    }

    /// <summary>Returns a snapshot of all recorded counters.</summary>
    public IReadOnlyDictionary<string, long> GetSnapshot() => new Dictionary<string, long>(_counters);

    /// <summary>Total number of recorded tool calls.</summary>
    public long TotalCalls => _counters.Values.Sum();
}
