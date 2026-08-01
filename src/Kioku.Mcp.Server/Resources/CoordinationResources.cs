using System.ComponentModel;
using Kioku.Mcp.Server.Domain.Coordination;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Resources;

/// <summary>
/// Read-only coordination resources for clients that prefer mounting durable state as context.
/// </summary>
public sealed class CoordinationResources(ICoordinationService coordination)
{
    [McpServerResource(
        UriTemplate = "kioku://coordination/work/{run_id}/{work_item_id}",
        Name = "coordination-work-item",
        MimeType = "application/json")]
    [Description("Current coordination projection, active claims, and unresolved conflicts for one work item.")]
    public async Task<string> GetWorkItem(string run_id, string work_item_id)
    {
        try
        {
            return await CoordinationJson.SerializeAsync(
                coordination.GetWorkItemAsync(run_id, work_item_id)).ConfigureAwait(false);
        }
        catch (CoordinationOperationException exception)
        {
            throw new McpException(exception.ToToolError());
        }
        catch (CoordinationStoreException)
        {
            throw new McpException(
                "[error:CORRUPT_HISTORY] Coordination history could not be read safely. Preserve the coordination files and inspect the history before retrying.");
        }
    }

    [McpServerResource(
        UriTemplate = "kioku://coordination/history/{run_id}/{work_item_id}",
        Name = "coordination-history",
        MimeType = "application/json")]
    [Description("Ordered immutable transition history for one coordination work item.")]
    public async Task<string> GetHistory(string run_id, string work_item_id)
    {
        try
        {
            return await CoordinationJson.SerializeAsync(
                coordination.ListHistoryAsync(run_id, work_item_id, limit: 200)).ConfigureAwait(false);
        }
        catch (CoordinationOperationException exception)
        {
            throw new McpException(exception.ToToolError());
        }
        catch (CoordinationStoreException)
        {
            throw new McpException(
                "[error:CORRUPT_HISTORY] Coordination history could not be read safely. Preserve the coordination files and inspect the history before retrying.");
        }
    }

    [McpServerResource(
        UriTemplate = "kioku://coordination/handoff/{run_id}/{work_item_id}",
        Name = "coordination-handoff",
        MimeType = "application/json")]
    [Description("Versioned handoff packet derived from durable coordination state and history.")]
    public async Task<string> GetHandoff(string run_id, string work_item_id)
    {
        try
        {
            return await CoordinationJson.SerializeAsync(
                coordination.GetHandoffPacketAsync(run_id, work_item_id)).ConfigureAwait(false);
        }
        catch (CoordinationOperationException exception)
        {
            throw new McpException(exception.ToToolError());
        }
        catch (CoordinationStoreException)
        {
            throw new McpException(
                "[error:CORRUPT_HISTORY] Coordination history could not be read safely. Preserve the coordination files and inspect the history before retrying.");
        }
    }
}

internal static class CoordinationJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    internal static async Task<string> SerializeAsync<T>(Task<T> operation)
    {
        var value = await operation.ConfigureAwait(false);
        return System.Text.Json.JsonSerializer.Serialize(value, Options);
    }
}
