using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Generates and stores semantic embeddings for vault notes using Ollama.
/// Embeddings are cached in {vault}/.kioku/embeddings.bin to survive restarts.
/// If Ollama is unavailable, the service degrades gracefully: IsAvailable = false.
/// Re-embedding a stale backlog (e.g. after a cache invalidation) runs in the background
/// with limited concurrency — it never blocks startup, since keyword search only needs
/// VaultIndexService's own index, not embeddings.
/// </summary>
public sealed class EmbeddingService(KiokuConfiguration config, ILogger<EmbeddingService> logger, IHttpClientFactory httpClientFactory)
    : IDisposable
{
    private const int FlushEvery = 50;
    private const int MaxConcurrentEmbeddings = 2;

    private readonly ConcurrentDictionary<string, EmbeddingEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _failedPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _embedSemaphore = new(MaxConcurrentEmbeddings);
    private readonly SemaphoreSlim _saveLock = new(1, 1);
    private readonly Stopwatch _sessionStopwatch = new();
    private int _pendingFlushes;
    private int _backlogCount;
    private int _embeddedThisSession;

    public bool IsAvailable { get; private set; }

    /// <summary>Number of embeddings currently cached in memory.</summary>
    public int CachedEmbeddingCount => _store.Count;

    /// <summary>
    /// Notes whose most recent embedding attempt failed (e.g. request timeout, content
    /// exceeding the model's context window) and were left out of the cache. Callers that
    /// wait for "all notes embedded" must account for this count too — a permanently failed
    /// note never increments <see cref="CachedEmbeddingCount"/>.
    /// </summary>
    public int FailedEmbeddingCount => _failedPaths.Count;

    /// <summary>Vault-relative paths of notes currently in <see cref="FailedEmbeddingCount"/>.</summary>
    public IReadOnlyCollection<string> FailedPaths => _failedPaths.Keys.ToArray();

    /// <summary>Configured embedding model name.</summary>
    public string EmbeddingModel => config.EmbeddingModel;

    /// <summary>Notes detected as needing an embedding (new or changed) that haven't finished yet.</summary>
    public int EmbeddingBacklog => Volatile.Read(ref _backlogCount);

    /// <summary>Notes embedded since this service started (used to compute <see cref="EmbeddingRatePerMinute"/>).</summary>
    public int EmbeddedThisSession => Volatile.Read(ref _embeddedThisSession);

    /// <summary>Rolling embedding throughput for this session, in notes per minute.</summary>
    public double EmbeddingRatePerMinute
    {
        get
        {
            var elapsedMinutes = _sessionStopwatch.Elapsed.TotalMinutes;
            return elapsedMinutes > 0 ? EmbeddedThisSession / elapsedMinutes : 0;
        }
    }

    /// <summary>
    /// Estimated time to clear the current backlog at the current rate. Null when the backlog
    /// is non-zero but no throughput has been observed yet (rate unknown).
    /// </summary>
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

    // Initialization

    public async Task InitializeAsync(IEnumerable<Note> existingNotes, CancellationToken cancellationToken = default)
    {
        IsAvailable = await PingOllamaAsync();
        if (!IsAvailable)
        {
            logger.Warn("Ollama not reachable at {Url} — semantic search disabled.", config.OllamaUrl);
            return;
        }

        logger.Info("Ollama reachable. Model: {Model}", config.EmbeddingModel);

        var (loaded, cachedModel, cachedDim) = await EmbeddingPersistence.LoadAsync(CachePath);

        // Invalidate cache if model or dimension changed
        if (loaded.Count > 0 && (cachedModel != config.EmbeddingModel || cachedDim != ExpectedDimension))
        {
            logger.Warn(
                "Embedding cache mismatch: cached model={CachedModel} dim={CachedDim}, " +
                "configured model={ConfigModel} dim={ConfigDim}. Invalidating cache.",
                cachedModel, cachedDim, config.EmbeddingModel, ExpectedDimension);
            loaded.Clear();
        }

        foreach (var (k, v) in loaded)
        {
            _store[k] = v;
        }

        logger.Info("Loaded {Count} cached embeddings from disk.", _store.Count);
        _sessionStopwatch.Start();

        // Re-embed notes whose content changed since last run, in the background — a large
        // backlog must never block startup, since keyword search doesn't need embeddings.
        var stale = existingNotes
            .Where(n => !_store.TryGetValue(n.VaultRelativePath, out var e) || e.Hash != n.ContentHash)
            .ToList();

        if (stale.Count > 0)
        {
            logger.Info(
                "Queuing {Count} new/changed notes for background embedding (up to {Parallelism} concurrent)...",
                stale.Count, MaxConcurrentEmbeddings);
            _ = ProcessBacklogAsync(stale, cancellationToken);
        }
    }

    // Public API

    public async Task IndexNoteAsync(Note note)
    {
        if (!IsAvailable)
        {
            return;
        }

        if (_store.TryGetValue(note.VaultRelativePath, out var existing) && existing.Hash == note.ContentHash)
        {
            return;
        }

        Interlocked.Increment(ref _backlogCount);
        try
        {
            await _embedSemaphore.WaitAsync();
            try
            {
                await EmbedAndStoreAsync(note);
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
            await SaveAsync();
        }
    }

    public void Remove(string filePath)
    {
        if (!IsAvailable)
        {
            return;
        }

        // filePath is absolute; store uses vault-relative paths
        var relative = Path.GetRelativePath(config.VaultPath, filePath);
        _store.TryRemove(relative, out _);
    }

    /// <summary>
    /// Re-keys a cached embedding after a file rename/move. The content hash is unchanged,
    /// so the chunks are reused and no re-embedding happens for a pure move.
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
            .Where(e => !e.VaultRelativePath.Equals(excludeVaultRelativePath, StringComparison.OrdinalIgnoreCase))
            .Select(entry =>
            {
                var absPath = Path.Combine(config.VaultPath, entry.VaultRelativePath);
                if (!notesByPath.TryGetValue(absPath, out var note))
                {
                    return null;
                }

                // Parent-document retrieval: a chunked note is represented by its
                // best-matching chunk (max-pooling), so results still aggregate to one
                // score per note regardless of how many chunks it was split into.
                var score = entry.Chunks.Max(c => CosineSimilarity(queryVector, c.Vector));
                return new SemanticResult(note, score);
            })
            .Where(r => r is not null && r.Score >= minScore)
            .Select(r => r!)
            .OrderByDescending(r => r.Score)
            .Take(maxResults);
    }

    /// <summary>
    /// Returns the raw embedding vector for a note by its vault-relative path.
    /// Returns null if not available or if Ollama is disabled. For a chunked note, returns
    /// only the first chunk's vector — a known limitation, fine for diagnostics
    /// (get_note_embedding) and FindSimilar's "most similar to this note" comparisons, but
    /// not a full representation of a multi-chunk note.
    /// </summary>
    public float[]? GetVector(string vaultRelativePath) =>
        _store.TryGetValue(vaultRelativePath, out var entry) ? entry.Chunks[0].Vector : null;

    /// <summary>
    /// Embeds a search query, applying the model's query task prefix when it requires one
    /// (e.g. "search_query: " for nomic-embed-text). Always use this for query-side
    /// embeddings so they live in the same space as the prefixed document embeddings.
    /// </summary>
    public Task<float[]?> EmbedQueryAsync(string query)
    {
        var prefix = ModelInfo.QueryPrefix;
        return EmbedAsync(string.IsNullOrEmpty(prefix) ? query : prefix + query);
    }

    public async Task<float[]?> EmbedAsync(string text)
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
                new OllamaEmbedRequest { Model = config.EmbeddingModel, Prompt = text });

            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync(OllamaJsonContext.Default.OllamaEmbedResponse);
            return result?.Embedding;
        }
        catch (Exception ex)
        {
            logger.Warn("Embedding request failed: {Message}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Persists the in-memory embedding cache to disk.
    /// Called automatically every <see cref="FlushEvery"/> embeddings and on graceful shutdown.
    /// Serialized via <see cref="_saveLock"/> — the periodic flush (from IndexNoteAsync) and
    /// the backlog's trailing save (from ProcessBacklogAsync) can otherwise race on the same
    /// ".tmp" file (opened with FileShare.None), losing whichever write loses the race.
    /// </summary>
    public async Task SaveAsync()
    {
        if (!IsAvailable || _store.Count == 0)
        {
            return;
        }

        await _saveLock.WaitAsync();
        try
        {
            await EmbeddingPersistence.SaveAsync(CachePath, _store, config.EmbeddingModel, ExpectedDimension);
            _pendingFlushes = 0;
            logger.Debug("Embedding cache saved. {Count} entries.", _store.Count);
        }
        catch (Exception ex)
        {
            logger.Warn("Could not save embedding cache: {Message}", ex.Message);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    // Private helpers

    /// <summary>
    /// Embeds a batch of stale notes with bounded concurrency (via IndexNoteAsync's own
    /// semaphore). Never throws — a failure here must not crash the background task silently
    /// nor take down the caller, since this runs detached from InitializeAsync's return.
    /// </summary>
    private async Task ProcessBacklogAsync(IReadOnlyList<Note> notes, CancellationToken cancellationToken)
    {
        // Held for the whole batch (on top of each note's own IndexNoteAsync increment) so
        // EmbeddingBacklog doesn't drop to 0 until the trailing SaveAsync below has actually
        // persisted the batch — otherwise a caller polling for "backlog cleared" as a signal
        // that the cache is safe to reload (e.g. a fresh EmbeddingService against the same
        // vault) can race the disk write and see an empty/stale cache.
        Interlocked.Increment(ref _backlogCount);
        try
        {
            var tasks = notes.Select(async note =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                await IndexNoteAsync(note);
            });

            await Task.WhenAll(tasks);
            await SaveAsync();
            logger.Info("Background embedding backlog complete. {Count} notes cached.", _store.Count);
        }
        catch (Exception ex)
        {
            logger.Warn("Background embedding backlog processing failed: {Message}", ex.Message);
        }
        finally
        {
            Interlocked.Decrement(ref _backlogCount);
        }
    }

    private async Task EmbedAndStoreAsync(Note note)
    {
        var prefix = ModelInfo.DocumentPrefix;
        var chunks = new List<EmbeddingChunk>();

        foreach (var chunk in NoteChunker.Chunk(note))
        {
            var text = string.IsNullOrEmpty(chunk.HeadingPath)
                ? chunk.Text
                : $"{chunk.HeadingPath}\n\n{chunk.Text}";
            if (!string.IsNullOrEmpty(prefix))
            {
                text = prefix + text;
            }

            var vector = await EmbedAsync(text);
            if (vector is not null)
            {
                chunks.Add(new EmbeddingChunk(chunk.HeadingPath, vector));
            }
        }

        if (chunks.Count > 0)
        {
            _store[note.VaultRelativePath] = new EmbeddingEntry(note.VaultRelativePath, note.ContentHash, chunks);
            _failedPaths.TryRemove(note.VaultRelativePath, out _);
        }
        else
        {
            _failedPaths[note.VaultRelativePath] = 0;
        }
    }

    private async Task<bool> PingOllamaAsync()
    {
        try
        {
            using var http = httpClientFactory.CreateClient("ollama");
            var response = await http.GetAsync($"{config.OllamaUrl}/api/tags");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Computes cosine similarity between two equal-length vectors.
    /// </summary>
    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            throw new InvalidOperationException(
                $"Vector dimension mismatch: query has {a.Length} dims, cached has {b.Length} dims. " +
                "This may indicate a model change. Delete .kioku/embeddings.bin to re-embed.");
        }

        var len = a.Length;
        var vectors = len / Vector<float>.Count;
        var remainder = vectors * Vector<float>.Count;

        var dotAccum = Vector<float>.Zero;
        var normAAccum = Vector<float>.Zero;
        var normBAccum = Vector<float>.Zero;

        for (int i = 0; i < vectors; i++)
        {
            var offset = i * Vector<float>.Count;
            var va = new Vector<float>(a.AsSpan(offset, Vector<float>.Count));
            var vb = new Vector<float>(b.AsSpan(offset, Vector<float>.Count));

            dotAccum += va * vb;
            normAAccum += va * va;
            normBAccum += vb * vb;
        }

        float dot = 0f, normA = 0f, normB = 0f;
        for (int i = 0; i < Vector<float>.Count; i++)
        {
            dot += dotAccum[i];
            normA += normAAccum[i];
            normB += normBAccum[i];
        }

        for (int i = remainder; i < len; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return dot / (MathF.Sqrt(normA) * MathF.Sqrt(normB) + 1e-8f);
    }

    public void Dispose()
    {
        // HttpClient instances are managed by IHttpClientFactory, no need to dispose here
    }
}

// Domain types

internal record EmbeddingChunk(string HeadingPath, float[] Vector);

internal record EmbeddingEntry(string VaultRelativePath, string Hash, IReadOnlyList<EmbeddingChunk> Chunks);

public record SemanticResult(Note Note, float Score);

// Ollama HTTP types (AOT-safe)

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
