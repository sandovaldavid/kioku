using Kioku.Mcp.Server.Domain.Coordination;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for safe, durable coordination conflict records.
/// </summary>
public interface ICoordinationConflictStore
{
    Task<CoordinationConflict> RecordAsync(
        CoordinationConflict conflict,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoordinationConflict>> ListAsync(
        string? runId = null,
        string? workItemId = null,
        string? status = null,
        CancellationToken cancellationToken = default);

    Task<CoordinationConflict> ResolveAsync(
        string conflictId,
        string status,
        string resolution,
        CoordinationActor actor,
        CancellationToken cancellationToken = default);
}
