using System.ComponentModel;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// Utility MCP Tools: diagnostics, index status, and health checks.
/// </summary>
[McpServerToolType]
public sealed class UtilityTools(VaultIndexService vault, KiokuConfiguration config)
{
    [McpServerTool, Description("Verifies that the Kioku MCP server is active and responding.")]
    public string ping()
    {
        return $"🟢 Kioku MCP Server v0.1.0 — Online\n" +
               $"📁 Vault: {config.VaultPath}\n" +
               $"📝 Indexed notes: {vault.IndexedCount}\n" +
               $"✅ Index ready: {(vault.IsReady ? "Yes" : "No (loading...)")}\n" +
               $"🕐 UTC: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss}";
    }

    [McpServerTool, Description(
        "Returns the current status of the in-memory index: " +
        "number of notes, last update time, and whether the index is ready.")]
    public string get_index_status()
    {
        return $"📊 Kioku Index Status\n" +
               $"   Indexed notes: {vault.IndexedCount}\n" +
               $"   Last indexed: {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
               $"   Status: {(vault.IsReady ? "✅ Ready" : "⏳ Loading...")}\n" +
               $"   Vault: {config.VaultPath}";
    }

    [McpServerTool, Description(
        "Forces a full re-indexing of the entire vault. " +
        "Useful if the index got out of sync or massive changes were made outside of Obsidian.")]
    public async Task<string> rebuild_index()
    {
        await vault.RebuildIndexAsync();
        return $"✅ Re-indexing completed. {vault.IndexedCount} notes indexed.\n" +
               $"🕐 Completed: {vault.LastIndexed.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
    }
}
