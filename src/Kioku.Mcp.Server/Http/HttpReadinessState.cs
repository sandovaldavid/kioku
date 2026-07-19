namespace Kioku.Mcp.Server.Http;

/// <summary>
/// Thread-safe startup state consumed by the protected readiness endpoint. Ollama-backed
/// capabilities are optional and may be degraded without making keyword-based MCP tools unready.
/// </summary>
public sealed class HttpReadinessState
{
    private int _indexState;
    private int _embeddingState;
    private int _generationState;

    public bool IsReady => Volatile.Read(ref _indexState) == (int)ComponentState.Ready;

    public void MarkIndexReady() =>
        Volatile.Write(ref _indexState, (int)ComponentState.Ready);

    public void MarkIndexFailed() =>
        Volatile.Write(ref _indexState, (int)ComponentState.Failed);

    public void SetOptionalDependencies(
        bool embeddingsAvailable,
        bool generationConfigured,
        bool generationAvailable)
    {
        Volatile.Write(
            ref _embeddingState,
            embeddingsAvailable ? (int)ComponentState.Ready : (int)ComponentState.Degraded);
        Volatile.Write(
            ref _generationState,
            !generationConfigured
                ? (int)ComponentState.Disabled
                : generationAvailable
                    ? (int)ComponentState.Ready
                    : (int)ComponentState.Degraded);
    }

    internal HttpReadinessSnapshot GetSnapshot() => new(
        IsReady,
        FormatState(Volatile.Read(ref _indexState)),
        FormatState(Volatile.Read(ref _embeddingState)),
        FormatState(Volatile.Read(ref _generationState)));

    private static string FormatState(int state) => (ComponentState)state switch
    {
        ComponentState.Ready => "ready",
        ComponentState.Degraded => "degraded",
        ComponentState.Disabled => "disabled",
        ComponentState.Failed => "failed",
        _ => "starting",
    };

    private enum ComponentState
    {
        Starting,
        Ready,
        Degraded,
        Disabled,
        Failed,
    }
}

internal sealed record HttpReadinessSnapshot(
    bool IsReady,
    string Index,
    string Embeddings,
    string Generation);
