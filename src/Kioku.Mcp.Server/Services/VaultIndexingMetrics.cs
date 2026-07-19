namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Thread-safe operational metrics for the vault indexing pipeline. The metrics contain only
/// counts and durations; note names and contents are never recorded.
/// </summary>
public sealed class VaultIndexingMetrics
{
    private long _queueDepth;
    private long _queuedChanges;
    private long _processedChanges;
    private long _failedChanges;
    private long _coalescedChanges;
    private long _reconciliationCount;
    private long _indexedFiles;
    private long _lastScanDurationTicks;
    private long _lastEmbeddingInitializationTicks;
    private int _activeOperations;
    private int _maximumObservedConcurrency;

    public VaultIndexingMetricsSnapshot Snapshot => new(
        QueueDepth: Volatile.Read(ref _queueDepth),
        QueuedChanges: Volatile.Read(ref _queuedChanges),
        ProcessedChanges: Volatile.Read(ref _processedChanges),
        FailedChanges: Volatile.Read(ref _failedChanges),
        CoalescedChanges: Volatile.Read(ref _coalescedChanges),
        ReconciliationCount: Volatile.Read(ref _reconciliationCount),
        IndexedFiles: Volatile.Read(ref _indexedFiles),
        LastScanDuration: TimeSpan.FromTicks(Volatile.Read(ref _lastScanDurationTicks)),
        LastEmbeddingInitializationDuration: TimeSpan.FromTicks(
            Volatile.Read(ref _lastEmbeddingInitializationTicks)),
        ActiveOperations: Volatile.Read(ref _activeOperations),
        MaximumObservedConcurrency: Volatile.Read(ref _maximumObservedConcurrency));

    internal void ChangeQueued()
    {
        Interlocked.Increment(ref _queuedChanges);
        Interlocked.Increment(ref _queueDepth);
    }

    internal void ChangeDequeued() => DecrementNonNegative(ref _queueDepth);

    internal void ChangeProcessed() => Interlocked.Increment(ref _processedChanges);

    internal void ChangeFailed() => Interlocked.Increment(ref _failedChanges);

    internal void ChangeCoalesced() => Interlocked.Increment(ref _coalescedChanges);

    internal void ReconciliationCompleted(TimeSpan duration, long indexedFiles)
    {
        Interlocked.Increment(ref _reconciliationCount);
        Interlocked.Exchange(ref _lastScanDurationTicks, duration.Ticks);
        Interlocked.Exchange(ref _indexedFiles, indexedFiles);
    }

    internal void EmbeddingInitializationCompleted(TimeSpan duration) =>
        Interlocked.Exchange(ref _lastEmbeddingInitializationTicks, duration.Ticks);

    internal IDisposable BeginOperation()
    {
        var current = Interlocked.Increment(ref _activeOperations);
        UpdateMaximum(current);
        return new OperationScope(this);
    }

    private void UpdateMaximum(int current)
    {
        while (true)
        {
            var observed = Volatile.Read(ref _maximumObservedConcurrency);
            if (current <= observed ||
                Interlocked.CompareExchange(ref _maximumObservedConcurrency, current, observed) == observed)
            {
                return;
            }
        }
    }

    private static void DecrementNonNegative(ref long value)
    {
        while (true)
        {
            var observed = Volatile.Read(ref value);
            if (observed == 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref value, observed - 1, observed) == observed)
            {
                return;
            }
        }
    }

    private sealed class OperationScope(VaultIndexingMetrics owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._activeOperations);
            }
        }
    }
}

public sealed record VaultIndexingMetricsSnapshot(
    long QueueDepth,
    long QueuedChanges,
    long ProcessedChanges,
    long FailedChanges,
    long CoalescedChanges,
    long ReconciliationCount,
    long IndexedFiles,
    TimeSpan LastScanDuration,
    TimeSpan LastEmbeddingInitializationDuration,
    int ActiveOperations,
    int MaximumObservedConcurrency);
