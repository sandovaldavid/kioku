namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Coordinates MCP tool execution with the deterministic cold vault scan without coupling the
/// Generic Host startup path to indexing latency. The gate represents cold-index readiness only;
/// embedding and generation warm-up remain optional dependencies reported separately.
/// </summary>
public sealed class VaultIndexReadinessGate
{
    private readonly TaskCompletionSource<bool> _ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsReady => _ready.Task.IsCompletedSuccessfully;

    public Task WaitAsync(CancellationToken cancellationToken = default) =>
        _ready.Task.WaitAsync(cancellationToken);

    internal void MarkReady() => _ready.TrySetResult(true);

    internal void MarkCanceled(CancellationToken cancellationToken) =>
        _ready.TrySetCanceled(cancellationToken);

    internal void MarkFailed(Exception exception) =>
        _ready.TrySetException(exception);
}