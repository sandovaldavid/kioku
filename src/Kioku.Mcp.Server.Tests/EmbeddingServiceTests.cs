using System.Net;
using System.Net.Http.Json;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Tests for EmbeddingService's background re-embedding backlog: hash-based skip across
/// restarts, non-blocking startup for a large backlog, and bounded concurrency against Ollama.
/// Each test gets its own temporary vault directory (just used for the cache file location).
/// </summary>
public class EmbeddingServiceTests : IAsyncLifetime
{
    private string _vaultPath = null!;

    public Task InitializeAsync()
    {
        _vaultPath = Path.Combine(Path.GetTempPath(), $"kioku-embed-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_vaultPath);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(_vaultPath, recursive: true);
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    private static Note MakeNote(string relativePath, string contentHash) => new()
    {
        FilePath = relativePath,
        VaultRelativePath = relativePath,
        Name = Path.GetFileNameWithoutExtension(relativePath),
        RawContent = "content",
        PlainText = "content",
        ContentHash = contentHash,
    };

    private EmbeddingService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder,
        string model = "nomic-embed-text")
    {
        var config = new KiokuConfiguration { VaultPath = _vaultPath, EmbeddingModel = model };
        return new EmbeddingService(config, NullLogger<EmbeddingService>.Instance, new FakeHttpClientFactory(new FakeHttpMessageHandler(responder)));
    }

    private static async Task WaitForBacklogToClearAsync(EmbeddingService service, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (service.EmbeddingBacklog > 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task InitializeAsync_NewNotes_EmbedsAllInBackgroundAndPersists()
    {
        var service = CreateService((req, _) => req.Method == HttpMethod.Get
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) }));

        var notes = new[] { MakeNote("A.md", "hash-a"), MakeNote("B.md", "hash-b") };

        await service.InitializeAsync(notes);
        await WaitForBacklogToClearAsync(service);

        Assert.Equal(0, service.EmbeddingBacklog);
        Assert.Equal(2, service.CachedEmbeddingCount);
        Assert.Equal(2, service.EmbeddedThisSession);
    }

    [Fact]
    public async Task InitializeAsync_CorruptedCacheFile_DoesNotThrowAndRebuildsInBackground()
    {
        // Simulates an unclean shutdown: valid magic header present, but the file is
        // truncated before the rest of the header can be read (see GitHub #441).
        var cachePath = Path.Combine(_vaultPath, ".kioku", "embeddings.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
        await File.WriteAllBytesAsync(cachePath, "KIOKU_EMB\n"u8.ToArray());

        var service = CreateService((req, _) => req.Method == HttpMethod.Get
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) }));

        var notes = new[] { MakeNote("A.md", "hash-a") };

        // Must not throw EndOfStreamException/IOException — the corrupted cache should be
        // discarded, not crash server startup.
        await service.InitializeAsync(notes);
        await WaitForBacklogToClearAsync(service);

        Assert.Equal(1, service.CachedEmbeddingCount); // re-embedded fresh after cache discard
    }

    [Fact]
    public async Task InitializeAsync_UnchangedNotesAcrossRestart_AreNotReEmbedded()
    {
        var embedCalls = 0;
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder = (req, _) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            Interlocked.Increment(ref embedCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) });
        };

        var notes = new[] { MakeNote("A.md", "hash-a"), MakeNote("B.md", "hash-b") };

        var first = CreateService(responder);
        await first.InitializeAsync(notes);
        await WaitForBacklogToClearAsync(first);
        Assert.Equal(2, embedCalls);

        // Fresh service instance, same vault path (same cache file), same notes/hashes.
        var second = CreateService(responder);
        await second.InitializeAsync(notes);
        await WaitForBacklogToClearAsync(second);

        Assert.Equal(2, embedCalls); // no new embed calls
        Assert.Equal(2, second.CachedEmbeddingCount); // loaded from disk
        Assert.Equal(0, second.EmbeddingBacklog);
    }

    [Fact]
    public async Task InitializeAsync_ChangedContentHash_IsReEmbedded()
    {
        var embedCalls = 0;
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder = (req, _) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            Interlocked.Increment(ref embedCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) });
        };

        var first = CreateService(responder);
        await first.InitializeAsync([MakeNote("A.md", "hash-v1")]);
        await WaitForBacklogToClearAsync(first);
        Assert.Equal(1, embedCalls);

        var second = CreateService(responder);
        await second.InitializeAsync([MakeNote("A.md", "hash-v2")]);
        await WaitForBacklogToClearAsync(second);

        Assert.Equal(2, embedCalls); // re-embedded due to hash mismatch
    }

    [Fact]
    public async Task InitializeAsync_LargeBacklog_ReturnsBeforeEmbeddingCompletes()
    {
        var service = CreateService(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            await Task.Delay(150, ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) };
        });

        var notes = Enumerable.Range(0, 5).Select(i => MakeNote($"Note{i}.md", $"hash-{i}")).ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await service.InitializeAsync(notes);
        sw.Stop();

        // 5 notes at 150ms each with 2-way parallelism would take ~375ms if InitializeAsync
        // waited for them; it must return almost immediately instead.
        Assert.True(sw.ElapsedMilliseconds < 100, $"InitializeAsync took {sw.ElapsedMilliseconds}ms — it should return before the backlog finishes.");
        Assert.True(service.EmbeddingBacklog > 0, "Backlog should still be non-zero right after InitializeAsync returns.");

        await WaitForBacklogToClearAsync(service);
        Assert.Equal(0, service.EmbeddingBacklog);
        Assert.Equal(5, service.CachedEmbeddingCount);
    }

    [Fact]
    public async Task IndexNoteAsync_ConcurrentCalls_NeverExceedTwoInFlightOllamaRequests()
    {
        var current = 0;
        var maxObserved = 0;

        var service = CreateService(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref maxObserved, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref current);

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) };
        });

        // Ping so IsAvailable becomes true, without going through InitializeAsync's own backlog.
        await service.InitializeAsync([]);

        var notes = Enumerable.Range(0, 6).Select(i => MakeNote($"Concurrent{i}.md", $"hash-{i}")).ToArray();
        await Task.WhenAll(notes.Select(note => service.IndexNoteAsync(note)));

        Assert.True(maxObserved <= 2, $"Observed {maxObserved} concurrent Ollama requests — expected at most 2.");
        Assert.Equal(6, service.CachedEmbeddingCount);
    }

    [Fact]
    public async Task IndexNoteAsync_UnchangedHash_SkipsReEmbedding()
    {
        var embedCalls = 0;
        var service = CreateService((req, _) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            Interlocked.Increment(ref embedCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) });
        });

        await service.InitializeAsync([]);

        var note = MakeNote("A.md", "same-hash");
        await service.IndexNoteAsync(note);
        await service.IndexNoteAsync(note); // same hash, second call must be a no-op

        Assert.Equal(1, embedCalls);
    }

    [Fact]
    public async Task IndexNoteAsyncConcurrentSameHashEmbedsOnlyOnce()
    {
        var embedCalls = 0;
        var firstRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = CreateService(async (req, ct) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            Interlocked.Increment(ref embedCalls);
            firstRequestStarted.TrySetResult();
            await releaseFirstRequest.Task.WaitAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }),
            };
        });

        await service.InitializeAsync([]);

        var note = MakeNote("A.md", "same-hash");
        var first = service.IndexNoteAsync(note);
        await firstRequestStarted.Task;
        var second = service.IndexNoteAsync(note);
        releaseFirstRequest.TrySetResult();

        await Task.WhenAll(first, second);

        Assert.Equal(1, embedCalls);
    }

    [Fact]
    public async Task IndexNoteAsync_MissingEntryAppearsBeforePathLock_DoesNotReenterDestinationLock()
    {
        var embedCalls = 0;
        using var service = CreateService(
            DeterministicEmbedding.Responder(_ => Interlocked.Increment(ref embedCalls)));
        await service.InitializeAsync([]);

        var addMissingEntry = 1;
        service.IndexNoteBeforePathLockAsync = () =>
            Interlocked.Exchange(ref addMissingEntry, 0) == 1
                ? service.IndexNoteAsync(MakeNote("Old.md", "same-hash"))
                : Task.CompletedTask;

        try
        {
            await service.IndexNoteAsync(MakeNote("New.md", "same-hash"))
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            service.IndexNoteBeforePathLockAsync = null;
        }

        Assert.Equal(2, embedCalls);
        Assert.NotNull(service.GetVector("Old.md"));
        Assert.NotNull(service.GetVector("New.md"));
    }

    [Fact]
    public async Task IndexNoteAsync_LongMultiChunkNote_CachedEmbeddingCountStaysNoteLevel()
    {
        var embedCalls = 0;
        var service = CreateService((req, _) =>
        {
            if (req.Method == HttpMethod.Get)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }

            Interlocked.Increment(ref embedCalls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embedding = new[] { 0.1f, 0.2f } }) });
        });

        await service.InitializeAsync([]);

        var longBody = string.Join("\n\n", Enumerable.Range(1, 5)
            .Select(i => $"## Section {i}\n" + string.Concat(Enumerable.Repeat($"word{i} ", 400))));
        var rawContent = $"# Long Note\n\n{longBody}";
        var note = new Note
        {
            FilePath = "Long.md",
            VaultRelativePath = "Long.md",
            Name = "Long Note",
            RawContent = rawContent,
            PlainText = MarkdownTextExtractor.Extract(rawContent, FrontmatterParser.GetBodyStart(rawContent)),
            ContentHash = "hash-long",
        };

        await service.IndexNoteAsync(note);

        Assert.True(embedCalls > 1, $"expected the long note to be split into multiple embedding calls, got {embedCalls}");
        // Despite N chunk-level Ollama calls, the store still counts this as ONE note.
        Assert.Equal(1, service.CachedEmbeddingCount);
    }

    [Fact]
    public async Task EstimatedTimeRemaining_ZeroBacklog_ReturnsZero()
    {
        var service = CreateService((req, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        await service.InitializeAsync([]);

        Assert.Equal(TimeSpan.Zero, service.EstimatedTimeRemaining);
    }

    [Fact]
    public async Task NomicModel_AppliesDocumentAndQueryPrefixes()
    {
        var prompts = new List<string>();
        var service = CreateService(DeterministicEmbedding.Responder(p =>
        {
            lock (prompts)
            {
                prompts.Add(p);
            }
        }));

        await service.InitializeAsync([]);
        await service.IndexNoteAsync(MakeNote("A.md", "hash-a"));
        await service.EmbedQueryAsync("burnout laboral");

        Assert.Contains(prompts, p => p.StartsWith("search_document: ", StringComparison.Ordinal));
        Assert.Contains(prompts, p => p == "search_query: burnout laboral");
    }

    [Fact]
    public async Task PrefixlessModel_SendsRawText()
    {
        var prompts = new List<string>();
        var service = CreateService(
            DeterministicEmbedding.Responder(p =>
            {
                lock (prompts)
                {
                    prompts.Add(p);
                }
            }),
            model: "bge-m3");

        await service.InitializeAsync([]);
        await service.IndexNoteAsync(MakeNote("A.md", "hash-a"));
        await service.EmbedQueryAsync("burnout laboral");

        Assert.All(prompts, p => Assert.DoesNotContain("search_document:", p));
        Assert.Contains("burnout laboral", prompts);
    }

    [Fact]
    public async Task SynchronizeFileMoveAsyncUnchangedContentReusesVectorWithoutReEmbedding()
    {
        var embedCalls = 0;
        var service = CreateService(DeterministicEmbedding.Responder(_ => Interlocked.Increment(ref embedCalls)));
        var config = new KiokuConfiguration { VaultPath = _vaultPath, EmbeddingModel = "nomic-embed-text" };
        using var vault = new VaultIndexService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VaultIndexService>.Instance, config, service);

        var oldPath = Path.Combine(_vaultPath, "Old.md");
        await File.WriteAllTextAsync(oldPath, "---\ntags: [prueba]\n---\ncontenido estable de la nota");
        await vault.InitializeAsync();
        await WaitForBacklogToClearAsync(service);

        var callsAfterIndexing = embedCalls;
        Assert.True(callsAfterIndexing > 0);
        Assert.NotNull(service.GetVector("Old.md"));

        var newPath = Path.Combine(_vaultPath, "Sub", "New.md");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Move(oldPath, newPath);
        await vault.SynchronizeFileMoveAsync(oldPath, newPath);
        await WaitForBacklogToClearAsync(service);

        Assert.Equal(callsAfterIndexing, embedCalls);
        Assert.NotNull(service.GetVector(Path.Combine("Sub", "New.md")));
        Assert.Null(service.GetVector("Old.md"));
    }

    [Fact]
    public async Task MoveCaseOnlyRenameUpdatesCachedVectorPath()
    {
        var service = CreateService(DeterministicEmbedding.Responder());
        await service.InitializeAsync([]);
        await service.IndexNoteAsync(MakeNote("Notes/Design.md", "stable-hash"));
        await WaitForBacklogToClearAsync(service);

        var oldPath = Path.Combine(_vaultPath, "Notes", "Design.md");
        var newPath = Path.Combine(_vaultPath, "Notes", "design.md");
        service.Move(oldPath, newPath);
        await service.SaveAsync();

        var cache = await EmbeddingPersistence.LoadAsync(Path.Combine(_vaultPath, ".kioku", "embeddings.bin"));
        Assert.DoesNotContain(cache.Entries.Keys, key => key.Equals("Notes/Design.md", StringComparison.Ordinal));
        Assert.Contains(cache.Entries.Keys, key => key.Equals("Notes/design.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WatcherDeleteBeforeExplicitMoveReusesVector()
    {
        var embedCalls = 0;
        var service = CreateService(DeterministicEmbedding.Responder(_ => Interlocked.Increment(ref embedCalls)));
        var config = new KiokuConfiguration { VaultPath = _vaultPath, EmbeddingModel = "nomic-embed-text" };
        using var vault = new VaultIndexService(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VaultIndexService>.Instance, config, service);
        var watcherDeleteReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowWatcherDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcherDeleteFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vault.WatcherDeleteBeforeEmbeddingRemoveAsync = async _ =>
        {
            watcherDeleteReached.TrySetResult();
            try
            {
                await allowWatcherDelete.Task;
            }
            finally
            {
                watcherDeleteFinished.TrySetResult();
            }
        };

        var oldPath = Path.Combine(_vaultPath, "Old.md");
        await File.WriteAllTextAsync(oldPath, "---\ntags: [prueba]\n---\ncontenido estable de la nota");
        await vault.InitializeAsync();
        Assert.True(await service.WaitForInitialBacklogAsync(TimeSpan.FromSeconds(5)));
        await WaitForBacklogToClearAsync(service);

        var callsAfterIndexing = embedCalls;
        var newPath = Path.Combine(_vaultPath, "Sub", "New.md");
        Directory.CreateDirectory(Path.GetDirectoryName(newPath)!);
        File.Copy(oldPath, newPath);
        File.Delete(oldPath);

        await WaitForConditionAsync(() => vault.GetNote(oldPath) is null);
        await watcherDeleteReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await vault.SynchronizeFileMoveAsync(oldPath, newPath);
        allowWatcherDelete.TrySetResult();
        await watcherDeleteFinished.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForBacklogToClearAsync(service);
        vault.WatcherDeleteBeforeEmbeddingRemoveAsync = null;

        Assert.Equal(callsAfterIndexing, embedCalls);
        Assert.NotNull(service.GetVector(Path.Combine("Sub", "New.md")));
        Assert.Null(service.GetVector("Old.md"));
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int initial, computed;
        do
        {
            initial = target;
            computed = Math.Max(initial, value);
        } while (Interlocked.CompareExchange(ref target, computed, initial) != initial);
    }

    private static async Task WaitForConditionAsync(
        Func<bool> condition,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition(), "The expected watcher state was not observed before the timeout.");
    }
}
