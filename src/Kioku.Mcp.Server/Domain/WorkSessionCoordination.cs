namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Optional coordination identifiers supplied when a work session is created or resumed.
/// Empty values preserve the legacy, uncoordinated session behavior.
/// </summary>
public sealed record WorkSessionCoordinationRequest(
    string? RunId = null,
    string? WorkItemId = null,
    string? AttemptId = null)
{
    public bool IsRequested =>
        !string.IsNullOrWhiteSpace(RunId) ||
        !string.IsNullOrWhiteSpace(WorkItemId) ||
        !string.IsNullOrWhiteSpace(AttemptId);

    public static WorkSessionCoordinationRequest? FromToolArguments(
        string runId,
        string workItemId,
        string attemptId) =>
        string.IsNullOrWhiteSpace(runId) &&
        string.IsNullOrWhiteSpace(workItemId) &&
        string.IsNullOrWhiteSpace(attemptId)
            ? null
            : new(runId, workItemId, attemptId);
}

/// <summary>
/// Server-owned coordination identifiers persisted as the current context of a session.
/// A single session may execute multiple work items; each work item still references the
/// session independently through its coordination projection.
/// </summary>
public sealed record WorkSessionCoordinationLink(
    string RunId,
    string WorkItemId,
    string AttemptId);
