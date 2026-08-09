using System.Collections.Concurrent;
using System.Threading.Channels;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Microsoft.Extensions.Options;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Owns the vault watcher and a bounded, coalescing indexing queue. Cold scans and runtime
/// changes use the same configurable worker limit, and watcher failures trigger reconciliation.
/// </summary>
public sealed class VaultIndexingPipeline : BackgroundService
{
    private const int QueueCapacity = 2048;
    private const int MaximumReadAttempts = 3;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMilliseconds(100);

    private readonly IVaultIndexOperations _index;
    private readonly VaultPathPolicy _paths;
    private readonly VaultConfigService _vaultConfig;
    private readonly IOptions<KiokuOptions> _options;
    private readonly HttpReadinessState _readiness;
    private readonly TimeProvider _timeProvider;
    private readonly VaultIndexingMetrics _metrics;
    private readonly ILogger<VaultIndexingPipeline> _logger;
    private readonly Channel<VaultFileChange> _changes;
    private readonly ConcurrentDictionary<string, PendingFileChange> _pending =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly TaskCompletionSource<bool> _initialized =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private FileSystemWatcher? _watcher;
    private int _watcherStarted;
    private int _reconciliationRequested;
    private int _ready;
    private long _lastScanUnixMilliseconds;

    public VaultIndexingPipeline(
        IVaultIndexOperations index,
        VaultPathPolicy paths,
        VaultConfigService vaultConfig,
        IOptions<KiokuOptions> options,
        HttpReadinessState readiness,
        TimeProvider timeProvider,
        VaultIndexingMetrics metrics,
        ILogger<VaultIndexingPipeline> logger)
    {
        _index = index;
        _paths = paths;
        _vaultConfig = vaultConfig;
        _options = options;
        _readiness = readiness;
        _timeProvider = timeProvider;
        _metrics = metrics;
        _logger = logger;
        _changes = Channel.CreateBounded<VaultFileChange>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    }

    public bool IsReady => Volatile.Read(ref _ready) == 1;

    public DateTimeOffset? LastScanUtc
    {
        get
        {
            var milliseconds = Volatile.Read(ref _lastScanUnixMilliseconds);
            return milliseconds == 0
                ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
    }

    public VaultIndexingMetricsSnapshot Metrics => _metrics.Snapshot;

    public IReadOnlyCollection<Note> GetNotesSnapshot() => _index.GetNotesSnapshot();

    /// <summary>
    /// Performs the deterministic cold scan. The watcher is enabled before enumeration so
    /// changes occurring during startup are queued and applied once the background loop starts.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized.Task.IsCompletedSuccessfully)
            {
                return;
            }

            EnsureWatcherStarted();
            await ReconcileCoreAsync("cold_start", cancellationToken);
            _initialized.TrySetResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _initialized.TrySetCanceled(cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            _initialized.TrySetException(exception);
            throw;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <summary>Runs a full self-healing scan without exposing the index as ready while rebuilding.</summary>
    public async Task ReconcileAsync(
        string reason = "manual",
        CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken);
        try
        {
            await ReconcileCoreAsync(reason, cancellationToken);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    internal void EnqueueChangeForTest(string filePath, string kind = "changed")
    {
        var changeKind = kind.ToLowerInvariant() switch
        {
            "created" => VaultFileChangeKind.Created,
            "deleted" => VaultFileChangeKind.Deleted,
            _ => VaultFileChangeKind.Changed,
        };
        Enqueue(new VaultFileChange(changeKind, Path.GetFullPath(filePath), null, _timeProvider.GetUtcNow()));
    }

    internal void RequestReconciliationForTest() => RequestReconciliation("simulated_watcher_error");

    internal async Task WaitForIdleAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = _timeProvider.GetUtcNow() + timeout;
        var stableObservations = 0;
        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _metrics.Snapshot;
            var idle = snapshot.QueueDepth == 0 &&
                snapshot.ActiveOperations == 0 &&
                _pending.IsEmpty &&
                Volatile.Read(ref _reconciliationRequested) == 0;
            stableObservations = idle ? stableObservations + 1 : 0;
            if (stableObservations >= 3)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException("The vault indexing pipeline did not become idle before the timeout.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await _initialized.Task.WaitAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Task.WhenAll(
                ReadChangesAsync(stoppingToken),
                FlushChangesAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal BackgroundService shutdown.
        }
        finally
        {
            while (_changes.Reader.TryRead(out var change))
            {
                _metrics.ChangeDequeued();
                AcceptChange(change);
            }

            using var drainTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await FlushPendingAsync(flushAll: true, drainTimeout.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.Warn(
                    "Indexing shutdown drain exceeded 10 seconds with {Pending} paths remaining.",
                    _pending.Count);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
        }

        _changes.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        _scanGate.Dispose();
        base.Dispose();
    }

    private async Task ReadChangesAsync(CancellationToken cancellationToken)
    {
        await foreach (var change in _changes.Reader.ReadAllAsync(cancellationToken))
        {
            _metrics.ChangeDequeued();
            AcceptChange(change);
        }
    }

    private void AcceptChange(VaultFileChange change)
    {
        if (change.Kind == VaultFileChangeKind.Reconcile)
        {
            Interlocked.Exchange(ref _reconciliationRequested, 1);
            return;
        }

        _pending.AddOrUpdate(
            change.Path,
            _ => new PendingFileChange(change.Kind, change.Path, change.OldPath, change.EnqueuedAt),
            (_, existing) =>
            {
                _metrics.ChangeCoalesced();
                return existing.Merge(change);
            });
    }

    private async Task FlushChangesAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, _timeProvider);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (Interlocked.Exchange(ref _reconciliationRequested, 0) == 1)
            {
                await ReconcileAsync("watcher_recovery", cancellationToken);
            }

            await FlushPendingAsync(flushAll: false, cancellationToken);
        }
    }

    private async Task FlushPendingAsync(bool flushAll, CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow() - DebounceDelay;
        var due = new List<PendingFileChange>();
        foreach (var (path, pending) in _pending)
        {
            if (!flushAll && pending.LastSeenAt > cutoff)
            {
                continue;
            }

            if (_pending.TryRemove(path, out var removed))
            {
                due.Add(removed);
            }
        }

        await Parallel.ForEachAsync(
            due,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = IndexConcurrency,
                CancellationToken = cancellationToken,
            },
            ApplyChangeAsync);
    }

    private async ValueTask ApplyChangeAsync(
        PendingFileChange change,
        CancellationToken cancellationToken)
    {
        using var operation = _metrics.BeginOperation();
        try
        {
            if (change.OldPath is not null && File.Exists(change.Path))
            {
                await ExecuteWithRetryAsync(
                    token => _index.MoveAsync(change.OldPath, change.Path, token),
                    change.Path,
                    cancellationToken);
            }
            else if (File.Exists(change.Path) && !IsExcludedPath(change.Path))
            {
                await ExecuteWithRetryAsync(
                    token => _index.ReindexAsync(change.Path, token),
                    change.Path,
                    cancellationToken);
            }
            else
            {
                if (change.OldPath is not null)
                {
                    _index.Delete(change.OldPath);
                }

                _index.Delete(change.Path);
            }

            _metrics.ChangeProcessed();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _metrics.ChangeFailed();
            _logger.Error(exception, "Indexing change failed for {File}.", change.Path);
            RequestReconciliation("change_failure");
        }
    }

    private async Task ReconcileCoreAsync(string reason, CancellationToken cancellationToken)
    {
        var startedAt = _timeProvider.GetTimestamp();
        Volatile.Write(ref _ready, 0);
        _index.SetReady(false);
        _readiness.MarkIndexRebuilding();
        _logger.Info(
            "Starting vault reconciliation ({Reason}) with maximum concurrency {Concurrency}.",
            reason,
            IndexConcurrency);

        try
        {
            var files = EnumerateMarkdownFiles().ToArray();
            var livePaths = files.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _index.GetNotesSnapshot()
                         .Select(note => note.FilePath)
                         .Where(path => !livePaths.Contains(path)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                _index.Delete(stale);
            }

            await Parallel.ForEachAsync(
                files,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = IndexConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (filePath, token) =>
                {
                    using var operation = _metrics.BeginOperation();
                    try
                    {
                        await ExecuteWithRetryAsync(
                            innerToken => _index.ReindexAsync(filePath, innerToken),
                            filePath,
                            token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        _metrics.ChangeFailed();
                        _logger.Error(exception, "Could not reconcile {File}.", filePath);
                    }
                });

            var duration = _timeProvider.GetElapsedTime(startedAt);
            Volatile.Write(
                ref _lastScanUnixMilliseconds,
                _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            _index.SetReady(true);
            Volatile.Write(ref _ready, 1);
            _metrics.ReconciliationCompleted(duration, files.Length);
            _readiness.MarkIndexReady();
            _logger.Info(
                "Vault reconciliation completed in {ElapsedMs:F0} ms. {Count} Markdown files observed.",
                duration.TotalMilliseconds,
                files.Length);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _index.SetReady(false);
            throw;
        }
        catch
        {
            _index.SetReady(false);
            _readiness.MarkIndexFailed();
            throw;
        }
    }

    private async Task ExecuteWithRetryAsync(
        Func<CancellationToken, Task> action,
        string filePath,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaximumReadAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await action(cancellationToken);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastFailure = exception;
                if (attempt == MaximumReadAttempts)
                {
                    break;
                }

                var delay = TimeSpan.FromMilliseconds(50 * attempt * attempt);
                _logger.Debug(
                    exception,
                    "Transient indexing failure for {File}; retrying attempt {Attempt}/{Maximum} after {DelayMs} ms.",
                    filePath,
                    attempt + 1,
                    MaximumReadAttempts,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new IOException(
            $"Indexing '{filePath}' failed after {MaximumReadAttempts} attempts.",
            lastFailure);
    }

    private IEnumerable<string> EnumerateMarkdownFiles() =>
        _paths.EnumerateVaultFiles("*.md", recursive: true)
            .Select(Path.GetFullPath)
            .Where(path => !IsExcludedPath(path));

    private bool IsExcludedPath(string filePath)
    {
        if (!_paths.IsInsideVault(filePath))
        {
            return true;
        }

        var relative = Path.GetRelativePath(_paths.VaultRoot, filePath);
        if (relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar])
            .Any(segment => segment.StartsWith('.')))
        {
            return true;
        }

        foreach (var excludedFolder in _vaultConfig.ExcludeFolders)
        {
            var normalized = excludedFolder
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            if (relative.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith(normalized + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureWatcherStarted()
    {
        if (Interlocked.Exchange(ref _watcherStarted, 1) == 1)
        {
            return;
        }

        _watcher = new FileSystemWatcher(_paths.VaultRoot)
        {
            Filter = "*.md",
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            IncludeSubdirectories = true,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Changed += (_, eventArgs) => QueuePath(VaultFileChangeKind.Changed, eventArgs.FullPath);
        _watcher.Created += (_, eventArgs) => QueuePath(VaultFileChangeKind.Created, eventArgs.FullPath);
        _watcher.Deleted += (_, eventArgs) => QueuePath(VaultFileChangeKind.Deleted, eventArgs.FullPath);
        _watcher.Renamed += (_, eventArgs) =>
        {
            if (IsExcludedPath(eventArgs.FullPath))
            {
                QueuePath(VaultFileChangeKind.Deleted, eventArgs.OldFullPath);
                return;
            }

            Enqueue(new VaultFileChange(
                VaultFileChangeKind.Renamed,
                Path.GetFullPath(eventArgs.FullPath),
                Path.GetFullPath(eventArgs.OldFullPath),
                _timeProvider.GetUtcNow()));
        };
        _watcher.Error += (_, eventArgs) =>
        {
            _logger.Warn(
                eventArgs.GetException(),
                "FileSystemWatcher overflow/error detected; scheduling a full reconciliation.");
            RequestReconciliation("watcher_error");
        };
        _watcher.EnableRaisingEvents = true;
        _logger.Info(
            "Bounded FileSystemWatcher pipeline active on {Path} with queue capacity {Capacity}.",
            _paths.VaultRoot,
            QueueCapacity);
    }

    private void QueuePath(VaultFileChangeKind kind, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (kind != VaultFileChangeKind.Deleted && IsExcludedPath(fullPath))
        {
            return;
        }

        Enqueue(new VaultFileChange(kind, fullPath, null, _timeProvider.GetUtcNow()));
    }

    private void Enqueue(VaultFileChange change)
    {
        if (_changes.Writer.TryWrite(change))
        {
            _metrics.ChangeQueued();
            return;
        }

        _logger.Warn(
            "Indexing queue reached capacity {Capacity}; scheduling reconciliation instead of dropping state silently.",
            QueueCapacity);
        Interlocked.Exchange(ref _reconciliationRequested, 1);
    }

    private void RequestReconciliation(string reason)
    {
        if (Interlocked.Exchange(ref _reconciliationRequested, 1) == 1)
        {
            return;
        }

        _logger.Warn("Vault reconciliation requested: {Reason}.", reason);
        if (_changes.Writer.TryWrite(new VaultFileChange(
                VaultFileChangeKind.Reconcile,
                _paths.VaultRoot,
                null,
                _timeProvider.GetUtcNow())))
        {
            _metrics.ChangeQueued();
        }
    }

    private int IndexConcurrency => Math.Clamp(_options.Value.IndexConcurrency, 1, 128);

    private sealed record PendingFileChange(
        VaultFileChangeKind Kind,
        string Path,
        string? OldPath,
        DateTimeOffset LastSeenAt)
    {
        internal PendingFileChange Merge(VaultFileChange next) => new(
            next.Kind,
            next.Path,
            next.OldPath ?? OldPath,
            next.EnqueuedAt);
    }

    private sealed record VaultFileChange(
        VaultFileChangeKind Kind,
        string Path,
        string? OldPath,
        DateTimeOffset EnqueuedAt);

    private enum VaultFileChangeKind
    {
        Changed,
        Created,
        Deleted,
        Renamed,
        Reconcile,
    }
}
