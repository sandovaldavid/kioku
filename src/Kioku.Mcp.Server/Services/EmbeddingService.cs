using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Generates and stores semantic embeddings for vault notes using Ollama.
/// Embeddings are cached in {vault}/.kioku/embeddings.bin to survive restarts.
/// If Ollama is unavailable, the service degrades gracefully: IsAvailable = false.
/// Re-embedding a stale backlog runs in the background with configurable bounded concurrency.
/// </summary>
public sealed class EmbeddingService : IDisposable
{
    private const int FlushEvery = 50;

    private readonly KiokuConfiguration config;
    private readonly ILogger<EmbeddingService> logger;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly int _embeddingConcurrency;
    private readonly ConcurrentDictionary<string, EmbeddingEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _failedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _embedSemaphore;
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Stopwatch _sessionStopwatch = new();
    private Task _initialBacklogTask = Task.CompletedTask;
    private int _pendingFlushes;
    private int _backlogCount;
    private int _embeddedThisSession;

    public EmbeddingService(
        KiokuConfiguration config,
        ILogger<EmbeddingService> logger,
        IHttpClientFactory httpClientFactory,
        IOptions<KiokuOptions>? options = null)
    {
        this.config = config;
        this.logger = logger;
        this.httpClientFactory = httpClientFactory;
        _embeddingConcurrency = Math.Clamp(options?.Value.EmbeddingConcurrency ?? 2, 1, 128);
        _embedSemaphore = new SemaphoreSlim(_embeddingConcurrency, _embeddingConcurrency);
    }

    public bool IsAvailable { get; private set; }

    /// <summary>Number of embeddings currently cached in memory.</summary>
    public int CachedEmbeddingCount => _store.Count;

    /// <summary>
    /// Notes whose most recent embedding attempt failed and were left out of the cache.
    /// </summary>
    public int FailedEmbeddingCount => _failedPaths.Count;

    public IReadOnlyCollection<string> FailedPaths => _failedPaths.Keys.ToArray();

    public string EmbeddingModel => config.EmbeddingModel;

    public int EmbeddingBacklog => Volatile.Read(ref _backlogCount);

    public int EmbeddedThisSession => Volatile.Read(ref _embeddedThisSession);

    public int MaximumConcurrency => _embeddingConcurrency;

    public bool IsInitialBacklogComplete =>
        Volatile.Read(ref _initialBacklogTask).IsCompleted;

    public async Task<bool> WaitForInitialBacklogAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await Volatile.Read(ref _initialBacklogTask)
                .WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public double EmbeddingRatePerMinute
    {
        get
        {
            var elapsedMinutes = _sessionStopwatch.Elapsed.TotalMinutes;
            return elapsedMinutes > 0 ? EmbeddedThisSession / elapsedMinutes : 0;
        }
    }

    public TimeSpan? EstimatedTimeRemaining
    {
        get
        {
            var backlog = EmbeddingBacklog;
            if (backlog == 0)
            {
                return TimeSpan.Zero;
            }

            var rate = EmbeddingRatePerMinute;
            return rate > 0 ? TimeSpan.FromMinutes(backlog / rate) : null;
        }
    }

    private EmbeddingModelInfo ModelInfo => EmbeddingModelRegistry.GetModelInfo(config.EmbeddingModel);

    private int ExpectedDimension => ModelInfo.Dimension;

    private string CachePath => Path.Combine(config.VaultPath, ".kioku", "embeddings.bin");

    public async Task InitializeAsync(
        IEnumerable<Note> existingNotes,
        CancellationToken cancellationToken = default)
    {
        IsAvailable = await PingOllamaAsync(cancellationToken);
        if (!IsAvailable)
        {
            logger.Warn("Ollama not reachable at {Url} — semantic search disabled.", config.OllamaUrl);
            return;
        }

        logger.Info("Ollama reachable. Model: {Model}", config.EmbeddingModel);
        cancellationToken.ThrowIfCancellationRequested();
        var (loaded, cachedModel, cachedDim) = await EmbeddingPersistence.LoadAsync(CachePath);

        if (loaded.Count > 0 &&
            (cachedModel != config.EmbeddingModel || cachedDim != ExpectedDimension))
        {
            logger.Warn(
                "Embedding cache mismatch: cached model={CachedModel} dim={CachedDim}, " +
                "configured model={ConfigModel} dim={ConfigDim}. Invalidating cache.",
                cachedModel,
                cachedDim,
                config.EmbeddingModel,
                ExpectedDimension);
            loaded.Clear();
        }

        foreach (var (key, value) in loaded)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _store[key] = value;
        }

        logger.Info("Loaded {Count} cached embeddings from disk.", _store.Count);
        _sessionStopwatch.Start();

        var stale = existingNotes
            .Where(note =>
                !_store.TryGetValue(note.VaultRelativePath, out var entry) ||
                entry.Hash != note.ContentHash)
            .ToList();

        if (stale.Count > 0)
        {
            logger.Info(
                "Queuing {Count} new/changed notes for background embedding (up to {Parallelism} concurrent)...",
                stale.Count,
                _embeddingConcurrency);
            Volatile.Write(
                ref _initialBacklogTask,
                ProcessBacklogAsync(stale, cancellationToken));
        }
    }

    public async Task IndexNoteAsync(
        Note note,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return;
        }

        if (_store.TryGetValue(note.VaultRelativePath, out var existing) &&
            existing.Hash == note.ContentHash)
        {
            return;
        }

        Interlocked.Increment(ref _backlogCount);
        try
        {
            await _embedSemaphore.WaitAsync(cancellationToken);
            try
            {
                await EmbedAndStoreAsync(note, cancellationToken);
                Interlocked.Increment(ref _embeddedThisSession);
            }
            finally
            {
                _embedSemaphore.Release();
            }
        }
        finally
        {
            Interlocked.Decrement(ref _backlogCount);
        }

        if (Interlocked.Increment(ref _pendingFlushes) % FlushEvery == 0)
        {
            await SaveAsync(cancellationToken);
        }
    }

    public void Remove(string filePath)
    {
        if (!IsAvailable)
        {
            return;
        }

        var relative = Path.GetRelativePath(config.VaultPath, filePath);
        _store.TryRemove(relative, out _);
    }

    /// <summary>
    /// Re-keys a cached embedding after a file rename/move. The content hash is unchanged.
    /// </summary>
    public void Move(string oldFilePath, string newFilePath)
    {
        if (!IsAvailable)
        {
            return;
        }

        var oldRelative = Path.GetRelativePath(config.VaultPath, oldFilePath);
        var newRelative = Path.GetRelativePath(config.VaultPath, newFilePath);
        if (_store.TryRemove(oldRelative, out var entry))
        {
            _store[newRelative] = entry with { VaultRelativePath = newRelative };
        }
    }

    public IEnumerable<SemanticResult> SearchByVector(
        float[] queryVector,
        int maxResults,
        string excludeVaultRelativePath,
        IReadOnlyDictionary<string, Note> notesByPath,
        float minScore = 0f)
    {
        return _store.Values
            .Where(entry =>
                !entry.VaultRelativePath.Equals(
                    excludeVaultRelativePath,
                    StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var absolutePath = Path.Combine(config.VaultPath, entry.VaultRelativePath);
                if (!notesByPath.TryGetValue(absolutePath, out var note))
                {
                    return null;
                }

                var score = entry.Chunks.Max(chunk => CosineSimilarity(queryVector, chunk.Vector));
                return new SemanticResult(note, score);
            })
            .Where(result => result is not null && result.Score >= minScore)
            .Select(result => result!)
            .OrderByDescending(result => result.Score)
            .Take(maxResults);
    }

    public float[]? GetVector(string vaultRelativePath) =>
        _store.TryGetValue(vaultRelativePath, out var entry)
            ? entry.Chunks[0].Vector
            : null;

    public Task<float[]?> EmbedQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var prefix = ModelInfo.QueryPrefix;
        return EmbedAsync(
            string.IsNullOrEmpty(prefix) ? query : prefix + query,
            cancellationToken);
    }

    public async Task<float[]?> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        if (!IsAvailable)
        {
            return null;
        }

        try
        {
            using var http = httpClientFactory.CreateClient("ollama");
            var response = await http.PostAsJsonAsync(
                $"{config.OllamaUrl}/api/embeddings",
                new OllamaEmbedRequest { Model = config.EmbeddingModel, Prompt = text },
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(
                OllamaJsonContext.Default.OllamaEmbedResponse,
                cancellationToken);
            return result?.Embedding;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warn("Embedding request failed: {Message}", exception.Message);
            return null;
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || _store.Count == 0)
        {
            return;
        }

        await _saveLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await EmbeddingPersistence.SaveAsync(
                CachePath,
                _store,
                config.EmbeddingModel,
                ExpectedDimension);
            _pendingFlushes = 0;
            logger.Debug("Embedding cache saved. {Count} entries.", _store.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.Warn("Could not save embedding cache: {Message}", exception.Message);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    private async Task ProcessBacklogAsync(
        IReadOnlyList<Note> notes,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _backlogCount);
        try
        {
            await Parallel.ForEachAsync(
                notes,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = _embeddingConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (note, token) => await IndexNoteAsync(note, token));
            await SaveAsync(cancellationToken);
            logger.Info(
                "Background embedding backlog complete. {Count} notes cached.",
                _store.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.Info("Background embedding backlog cancelled during shutdown.");
        }
        catch (Exception exception)
        {
            logger.Warn(
                "Background embedding backlog processing failed: {Message}",
                exception.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _backlogCount);
        }
    }

    private async Task EmbedAndStoreAsync(Note note, CancellationToken cancellationToken)
    {
        var prefix = ModelInfo.DocumentPrefix;
        var chunks = new List<EmbeddingChunk>();

        foreach (var chunk in NoteChunker.Chunk(note))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = string.IsNullOrEmpty(chunk.HeadingPath)
                ? chunk.Text
                : $"{chunk.HeadingPath}\n\n{chunk.Text}";
            if (!string.IsNullOrEmpty(prefix))
            {
                text = prefix + text;
            }

            var vector = await EmbedAsync(text, cancellationToken);
            if (vector is not null)
            {
                chunks.Add(new EmbeddingChunk(chunk.HeadingPath, vector));
            }
        }

        if (chunks.Count > 0)
        {
            _store[note.VaultRelativePath] = new EmbeddingEntry(
                note.VaultRelativePath,
                note.ContentHash,
                chunks);
            _failedPaths.TryRemove(note.VaultRelativePath, out _);
        }
        else
        {
            _failedPaths[note.VaultRelativePath] = 0;
        }
    }

    private async Task<bool> PingOllamaAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var http = httpClientFactory.CreateClient("ollama");
            var response = await http.GetAsync(
                $"{config.OllamaUrl}/api/tags",
                cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new InvalidOperationException(
                $"Vector dimension mismatch: query has {a.Length} dims, cached has {b.Length} dims. " +
                "This may indicate a model change. Delete .kioku/embeddings.bin to re-embed.");
        }

        var length = a.Length;
        var vectorCount = length / Vector<float>.Count;
        var remainder = vectorCount * Vector<float>.Count;

        var dotAccumulator = Vector<float>.Zero;
        var normAAccumulator = Vector<float>.Zero;
        var normBAccumulator = Vector<float>.Zero;

        for (var index = 0; index < vectorCount; index++)
        {
            var offset = index * Vector<float>.Count;
            var vectorA = new Vector<float>(a.AsSpan(offset, Vector<float>.Count));
            var vectorB = new Vector<float>(b.AsSpan(offset, Vector<float>.Count));
            dotAccumulator += vectorA * vectorB;
            normAAccumulator += vectorA * vectorA;
            normBAccumulator += vectorB * vectorB;
        }

        float dot = 0;
        float normA = 0;
        float normB = 0;
        for (var index = 0; index < Vector<float>.Count; index++)
        {
            dot += dotAccumulator[index];
            normA += normAAccumulator[index];
            normB += normBAccumulator[index];
        }

        for (var index = remainder; index < length; index++)
        {
            dot += a[index] * b[index];
            normA += a[index] * a[index];
            normB += b[index] * b[index];
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
    }

    public void Dispose()
    {
        _embedSemaphore.Dispose();
        _saveLock.Dispose();
    }
}

internal record EmbeddingChunk(string HeadingPath, float[] Vector);

internal record EmbeddingEntry(
    string VaultRelativePath,
    string Hash,
    IReadOnlyList<EmbeddingChunk> Chunks);

public record SemanticResult(Note Note, float Score);

internal sealed class OllamaEmbedRequest
{
    [System.Text.Json.Serialization.JsonPropertyName("model")]
    public required string Model { get; init; }

    [System.Text.Json.Serialization.JsonPropertyName("prompt")]
    public required string Prompt { get; init; }
}

internal sealed class OllamaEmbedResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("embedding")]
    public float[]? Embedding { get; init; }
}

[JsonSerializable(typeof(OllamaEmbedRequest))]
[JsonSerializable(typeof(OllamaEmbedResponse))]
internal partial class OllamaJsonContext : JsonSerializerContext
{
}
