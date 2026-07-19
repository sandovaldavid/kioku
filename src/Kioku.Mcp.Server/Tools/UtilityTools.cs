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
    MetricsService? metrics = null,
    VaultConfigService? vaultConfig = null,
    VaultIndexingPipeline? indexing = null)
{
    [McpServerTool, Description(
        "Returns the current Kioku server health and status: vault path, indexed note count, " +
        "cached embeddings, Ollama availability, last update time, index readiness, and — if " +
        "a re-embedding backlog is being processed in the background — its progress (backlog, " +
        "rate, ETA).")]
    public string get_server_status()
    {
        var indexReady = indexing?.IsReady ?? vault.IsReady;
        var indexedCount = indexing?.GetNotesSnapshot().Count ?? vault.IndexedCount;
        DateTimeOffset? lastIndexed = indexing?.LastScanUtc;
        if (lastIndexed is null && vault.LastIndexed != default)
        {
            lastIndexed = vault.LastIndexed;
        }

        var result = $"[online] Kioku MCP Server\n" +
                     $"Health: healthy\n" +
                     $"   Vault: {config.VaultPath}\n" +
                     $"   Indexed notes: {indexedCount}\n" +
                     $"   Cached embeddings: {embedding?.CachedEmbeddingCount ?? 0}\n" +
                     $"   Ollama: {(embedding?.IsAvailable == true ? "[ok] Available" : "[info] Unavailable")}\n" +
                     $"   Embedding model: {config.EmbeddingModel}\n" +
                     $"   Last indexed: {FormatLastIndexed(lastIndexed)}\n" +
                     $"   Status: {(indexReady ? "[ok] Ready" : "[loading] Loading/rebuilding...")}\n" +
                     $"   Index ready: {(indexReady ? "Yes" : "No (loading or rebuilding)")}";

        if (indexing is not null)
        {
            var indexMetrics = indexing.Metrics;
            result += $"\n   Indexing pipeline:" +
                      $"\n      Queue depth: {indexMetrics.QueueDepth}" +
                      $"\n      Processed changes: {indexMetrics.ProcessedChanges}" +
                      $"\n      Failed changes: {indexMetrics.FailedChanges}" +
                      $"\n      Coalesced changes: {indexMetrics.CoalescedChanges}" +
                      $"\n      Reconciliations: {indexMetrics.ReconciliationCount}" +
                      $"\n      Last scan: {indexMetrics.LastScanDuration.TotalMilliseconds:F0} ms" +
                      $"\n      Max concurrency observed: {indexMetrics.MaximumObservedConcurrency}";
        }

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

        if (vaultConfig is not null)
        {
            result += "\n   Capabilities:" +
                      $"\n      Enabled: {string.Join(", ", vaultConfig.EnabledCapabilityGroups)}" +
                      $"\n      Disabled: {string.Join(", ", vaultConfig.DisabledCapabilityGroups)}" +
                      $"\n      Removed: {string.Join(", ", vaultConfig.RemovedCapabilityGroups)}" +
                      "\n      Changes require server restart";
        }

        result += $"\nUTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
        return result;
    }

    private static string FormatLastIndexed(DateTimeOffset? value) =>
        value is null ? "not completed" : $"{value.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

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

        return ts.TotalHours >= 1
            ? $"{ts.TotalHours:F1}h"
            : ts.TotalMinutes >= 1
                ? $"{ts.TotalMinutes:F1}m"
                : $"{ts.TotalSeconds:F0}s";
    }

    [McpServerTool, Description(
        "Forces a full re-indexing of the entire vault. " +
        "Useful if the index got out of sync or massive changes were made outside of Obsidian.")]
    public async Task<string> rebuild_index()
    {
        if (indexing is not null)
        {
            await indexing.ReconcileAsync("mcp_tool");
            return $"[ok] Re-indexing completed. {indexing.GetNotesSnapshot().Count} notes indexed.\n" +
                   $"Completed: {FormatLastIndexed(indexing.LastScanUtc)}";
        }

        await vault.RebuildIndexAsync();
        return $"[ok] Re-indexing completed. {vault.IndexedCount} notes indexed.\n" +
               $"Completed: {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }
}