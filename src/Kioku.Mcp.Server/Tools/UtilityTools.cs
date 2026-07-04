using System.ComponentModel;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// Utility MCP Tools: diagnostics, index status, and health checks.
/// </summary>
[McpServerToolType]
public sealed class UtilityTools(
    VaultIndexService vault,
    KiokuConfiguration config,
    EmbeddingService? embedding = null,
    MetricsService? metrics = null)
{
    [McpServerTool, Description("Verifies that the Kioku MCP server is active and responding.")]
    public string ping()
    {
        var result = $"[online] Kioku MCP Server\n" +
                     $"Vault: {config.VaultPath}\n" +
                     $"Indexed notes: {vault.IndexedCount}\n" +
                     $"Cached embeddings: {embedding?.CachedEmbeddingCount ?? 0}\n" +
                     $"Ollama: {(embedding?.IsAvailable == true ? "available" : "unavailable")}\n" +
                     $"Index ready: {(vault.IsReady ? "Yes" : "No (loading...)")}";

        if (metrics?.Enabled == true)
        {
            result += $"\nMetrics: enabled, total tool calls: {metrics.TotalCalls}";
        }

        result += $"\nUTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
        return result;
    }

    [McpServerTool, Description(
        "Returns the current status of the in-memory index: " +
        "number of notes, embeddings cached, Ollama availability, " +
        "last update time, whether the index is ready, and — if a re-embedding backlog " +
        "is being processed in the background — its progress (backlog, rate, ETA).")]
    public string get_index_status()
    {
        var result = $"Kioku Index Status\n" +
                     $"   Indexed notes: {vault.IndexedCount}\n" +
                     $"   Cached embeddings: {embedding?.CachedEmbeddingCount ?? 0}\n" +
                     $"   Ollama: {(embedding?.IsAvailable == true ? "[ok] Available" : "[info] Unavailable")}\n" +
                     $"   Embedding model: {config.EmbeddingModel}\n" +
                     $"   Last indexed: {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
                     $"   Status: {(vault.IsReady ? "[ok] Ready" : "[loading] Loading...")}\n" +
                     $"   Vault: {config.VaultPath}";

        if (embedding?.IsAvailable == true)
        {
            result += $"\n   Embedding backlog: {embedding.EmbeddingBacklog}\n" +
                      $"   Embedded this session: {embedding.EmbeddedThisSession}\n" +
                      $"   Embedding rate: {embedding.EmbeddingRatePerMinute:F1} notes/min\n" +
                      $"   Estimated remaining: {FormatEstimatedRemaining(embedding.EstimatedTimeRemaining)}";
        }

        if (metrics?.Enabled == true)
        {
            result += $"\n   Metrics: enabled, total tool calls: {metrics.TotalCalls}";
        }

        return result;
    }

    private static string FormatEstimatedRemaining(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return "unknown (still measuring rate)";
        }

        var ts = remaining.Value;
        if (ts <= TimeSpan.Zero)
        {
            return "0s (backlog clear)";
        }

        return ts.TotalHours >= 1 ? $"{ts.TotalHours:F1}h" : ts.TotalMinutes >= 1 ? $"{ts.TotalMinutes:F1}m" : $"{ts.TotalSeconds:F0}s";
    }

    [McpServerTool, Description(
        "Forces a full re-indexing of the entire vault. " +
        "Useful if the index got out of sync or massive changes were made outside of Obsidian.")]
    public async Task<string> rebuild_index()
    {
        await vault.RebuildIndexAsync();
        return $"[ok] Re-indexing completed. {vault.IndexedCount} notes indexed.\n" +
               $"Completed: {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }
}
