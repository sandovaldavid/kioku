using System.Collections.Concurrent;
using System.Globalization;
using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class VaultIndexingPipelineTests
{
    [Fact]
    public async Task Cold_start_never_exceeds_configured_concurrency()
    {
        using var vault = new TemporaryVault();
        vault.CreateNotes(500);
        var (pipeline, index, _) = CreatePipeline(vault.Path, concurrency: 4, operationDelay: TimeSpan.FromMilliseconds(2));
        await using var lifetime = new PipelineLifetime(pipeline);

        await pipeline.InitializeAsync(CancellationToken.None);

        Assert.Equal(500, index.GetNotesSnapshot().Count);
        Assert.InRange(index.MaximumObservedConcurrency, 1, 4);
        Assert.InRange(pipeline.Metrics.MaximumObservedConcurrency, 1, 4);
        Assert.Equal(1, index.FinalizeReconciliationCalls);
        Assert.True(index.IsReady);
        Assert.True(pipeline.IsReady);
    }

    [Fact]
    public async Task Rapid_changes_to_one_note_are_coalesced_into_one_effective_reindex()
    {
        using var vault = new TemporaryVault();
        var notePath = vault.CreateNote("burst.md", "initial");
        var (pipeline, index, _) = CreatePipeline(vault.Path, concurrency: 2, operationDelay: TimeSpan.FromMilliseconds(5));
        await using var lifetime = new PipelineLifetime(pipeline);
        await pipeline.InitializeAsync(CancellationToken.None);
        await pipeline.StartAsync(CancellationToken.None);
        var callsAfterColdStart = index.GetReindexCalls(notePath);

        for (var change = 0; change < 100; change++)
        {
            pipeline.EnqueueChangeForTest(notePath);
        }

        await pipeline.WaitForIdleAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(callsAfterColdStart + 1, index.GetReindexCalls(notePath));
        Assert.Equal(1, index.FinalizeReconciliationCalls);
        Assert.True(pipeline.Metrics.CoalescedChanges >= 99);
    }

    [Fact]
    public async Task Watcher_error_requests_a_reconciliation_that_indexes_missed_files()
    {
        using var vault = new TemporaryVault();
        vault.CreateNote("existing.md", "existing");
        var (pipeline, index, _) = CreatePipeline(vault.Path, concurrency: 2, operationDelay: TimeSpan.FromMilliseconds(2));
        await using var lifetime = new PipelineLifetime(pipeline);
        await pipeline.InitializeAsync(CancellationToken.None);
        await pipeline.StartAsync(CancellationToken.None);

        var missedPath = vault.CreateNote("missed.md", "created while watcher state was uncertain");
        pipeline.RequestReconciliationForTest();
        await pipeline.WaitForIdleAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(index.GetNotesSnapshot(), note =>
            note.FilePath.Equals(missedPath, StringComparison.OrdinalIgnoreCase));
        Assert.True(pipeline.Metrics.ReconciliationCount >= 2);
        Assert.True(index.FinalizeReconciliationCalls >= 2);
    }

    [Fact]
    public async Task Cancellation_stops_scheduling_new_cold_start_work_without_marking_failure()
    {
        using var vault = new TemporaryVault();
        vault.CreateNotes(1000);
        var readiness = new HttpReadinessState();
        var (pipeline, index, _) = CreatePipeline(
            vault.Path,
            concurrency: 3,
            operationDelay: TimeSpan.FromMilliseconds(25),
            readiness);
        await using var lifetime = new PipelineLifetime(pipeline);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(75));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            pipeline.InitializeAsync(cancellation.Token));

        Assert.True(index.GetNotesSnapshot().Count < 1000);
        Assert.False(index.IsReady);
        Assert.False(pipeline.IsReady);
        Assert.Equal("rebuilding", readiness.GetSnapshot().Index);
        Assert.Equal(0, index.FinalizeReconciliationCalls);
        Assert.InRange(index.MaximumObservedConcurrency, 0, 3);
    }

    [Fact]
    public async Task Reconciliation_removes_deleted_notes_and_reindexes_recreated_paths()
    {
        using var vault = new TemporaryVault();
        var path = vault.CreateNote("lifecycle.md", "v1");
        var (pipeline, index, _) = CreatePipeline(vault.Path, concurrency: 2, operationDelay: TimeSpan.FromMilliseconds(2));
        await using var lifetime = new PipelineLifetime(pipeline);
        await pipeline.InitializeAsync(CancellationToken.None);

        File.Delete(path);
        await pipeline.ReconcileAsync("delete_test");
        Assert.DoesNotContain(index.GetNotesSnapshot(), note =>
            note.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase));

        await File.WriteAllTextAsync(path, "v2");
        await pipeline.ReconcileAsync("recreate_test");
        Assert.Contains(index.GetNotesSnapshot(), note =>
            note.FilePath.Equals(path, StringComparison.OrdinalIgnoreCase) && note.RawContent == "v2");
        Assert.Equal(3, index.FinalizeReconciliationCalls);
    }

    private static (VaultIndexingPipeline Pipeline, FakeVaultIndexOperations Index, VaultIndexingMetrics Metrics)
        CreatePipeline(
            string vaultPath,
            int concurrency,
            TimeSpan operationDelay,
            HttpReadinessState? readiness = null)
    {
        var configuration = new KiokuConfiguration { VaultPath = vaultPath };
        var options = Options.Create(new KiokuOptions
        {
            VaultPath = vaultPath,
            IndexConcurrency = concurrency,
        });
        var paths = new VaultPathPolicy(configuration);
        var vaultConfig = new VaultConfigService(
            configuration,
            NullLogger<VaultConfigService>.Instance);
        var index = new FakeVaultIndexOperations(vaultPath, operationDelay);
        var metrics = new VaultIndexingMetrics();
        var pipeline = new VaultIndexingPipeline(
            index,
            paths,
            vaultConfig,
            options,
            readiness ?? new HttpReadinessState(),
            TimeProvider.System,
            metrics,
            NullLogger<VaultIndexingPipeline>.Instance);
        return (pipeline, index, metrics);
    }

    private sealed class FakeVaultIndexOperations(string vaultPath, TimeSpan operationDelay)
        : IVaultIndexOperations
    {
        private readonly ConcurrentDictionary<string, Note> _notes =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, int> _reindexCalls =
            new(StringComparer.OrdinalIgnoreCase);
        private int _active;
        private int _maximumObservedConcurrency;
        private int _finalizeReconciliationCalls;

        public bool IsReady { get; private set; }

        public int MaximumObservedConcurrency => Volatile.Read(ref _maximumObservedConcurrency);

        public int FinalizeReconciliationCalls => Volatile.Read(ref _finalizeReconciliationCalls);

        public int GetReindexCalls(string path) => _reindexCalls.GetValueOrDefault(Path.GetFullPath(path));

        public IReadOnlyCollection<Note> GetNotesSnapshot() => _notes.Values.ToArray();

        public void SetReady(bool ready) => IsReady = ready;

        public async Task ReindexAsync(string filePath, CancellationToken cancellationToken)
        {
            filePath = Path.GetFullPath(filePath);
            _reindexCalls.AddOrUpdate(filePath, 1, (_, value) => value + 1);
            using var operation = BeginOperation();
            await Task.Delay(operationDelay, cancellationToken);
            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            _notes[filePath] = CreateNote(filePath, content);
        }

        public Task ReconcileFileAsync(string filePath, CancellationToken cancellationToken) =>
            ReindexAsync(filePath, cancellationToken);

        public async Task MoveAsync(
            string oldPath,
            string newPath,
            CancellationToken cancellationToken)
        {
            using var operation = BeginOperation();
            await Task.Delay(operationDelay, cancellationToken);
            _notes.TryRemove(Path.GetFullPath(oldPath), out _);
            if (File.Exists(newPath))
            {
                var content = await File.ReadAllTextAsync(newPath, cancellationToken);
                _notes[Path.GetFullPath(newPath)] = CreateNote(newPath, content);
            }
        }

        public void Delete(string filePath) => _notes.TryRemove(Path.GetFullPath(filePath), out _);

        public void DeleteStale(string filePath) => Delete(filePath);

        public void FinalizeReconciliation() => Interlocked.Increment(ref _finalizeReconciliationCalls);

        private DelegateDisposable BeginOperation()
        {
            var current = Interlocked.Increment(ref _active);
            while (true)
            {
                var observed = Volatile.Read(ref _maximumObservedConcurrency);
                if (current <= observed ||
                    Interlocked.CompareExchange(ref _maximumObservedConcurrency, current, observed) == observed)
                {
                    break;
                }
            }

            return new DelegateDisposable(() => Interlocked.Decrement(ref _active));
        }

        private Note CreateNote(string filePath, string content) => new()
        {
            FilePath = Path.GetFullPath(filePath),
            VaultRelativePath = Path.GetRelativePath(vaultPath, filePath).Replace('\\', '/'),
            Name = System.IO.Path.GetFileNameWithoutExtension(filePath),
            RawContent = content,
            PlainText = content,
            ContentHash = content.GetHashCode(StringComparison.Ordinal).ToString(CultureInfo.InvariantCulture),
            LastModified = File.GetLastWriteTimeUtc(filePath),
        };
    }

    private sealed class TemporaryVault : IDisposable
    {
        public TemporaryVault()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"kioku-index-pipeline-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateNote(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public void CreateNotes(int count)
        {
            for (var index = 0; index < count; index++)
            {
                CreateNote($"notes/note-{index:D5}.md", $"content {index}");
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Windows can release FileSystemWatcher handles shortly after Dispose.
            }
            catch (UnauthorizedAccessException)
            {
                // Best-effort test cleanup only.
            }
        }
    }

    private sealed class PipelineLifetime(VaultIndexingPipeline pipeline) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await pipeline.StopAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The background service was not started in every test.
            }

            pipeline.Dispose();
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                dispose();
            }
        }
    }
}
