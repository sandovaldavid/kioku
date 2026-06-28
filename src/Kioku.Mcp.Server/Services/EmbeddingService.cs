using System.Net.Http.Json;
using System.Numerics;
using System.Text;
using System.Text.Json.Serialization;
using Kioku.Mcp.Server.Domain;
using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Generates and stores semantic embeddings for vault notes using Ollama.
/// Embeddings are cached in {vault}/.kioku/embeddings.bin to survive restarts.
/// If Ollama is unavailable, the service degrades gracefully: IsAvailable = false.
/// </summary>
public sealed class EmbeddingService(KiokuConfiguration config, ILogger<EmbeddingService> logger, IHttpClientFactory httpClientFactory)
    : IDisposable
{
    private readonly Dictionary<string, EmbeddingEntry> _store = new(StringComparer.OrdinalIgnoreCase);
    private int _pendingFlushes;
    private const int FlushEvery = 50;

    public bool IsAvailable { get; private set; }

    /// <summary>Number of embeddings currently cached in memory.</summary>
    public int CachedEmbeddingCount => _store.Count;

    /// <summary>Configured embedding model name.</summary>
    public string EmbeddingModel => config.EmbeddingModel;

    private int ExpectedDimension => EmbeddingModelRegistry.GetExpectedDimension(config.EmbeddingModel);

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

        // Re-embed notes whose content changed since last run
        var stale = existingNotes
            .Where(n => !_store.TryGetValue(n.VaultRelativePath, out var e) || e.Hash != n.ContentHash)
            .ToList();

        if (stale.Count > 0)
        {
            logger.Info("Embedding {Count} new/changed notes...", stale.Count);
            foreach (var note in stale)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                await EmbedAndStoreAsync(note);
            }

            await SaveAsync();
        }

        logger.Info("Embedding index ready. {Count} notes indexed.", _store.Count);
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

        await EmbedAndStoreAsync(note);

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
        _store.Remove(relative);
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

                return new SemanticResult(note, CosineSimilarity(queryVector, entry.Vector));
            })
            .Where(r => r is not null && r.Score >= minScore)
            .Select(r => r!)
            .OrderByDescending(r => r.Score)
            .Take(maxResults);
    }

    /// <summary>
    /// Returns the raw embedding vector for a note by its vault-relative path.
    /// Returns null if not available or if Ollama is disabled.
    /// </summary>
    public float[]? GetVector(string vaultRelativePath) =>
        _store.TryGetValue(vaultRelativePath, out var entry) ? entry.Vector : null;

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
    /// </summary>
    public async Task SaveAsync()
    {
        if (!IsAvailable || _store.Count == 0)
        {
            return;
        }

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
    }

    // Private helpers

    private async Task EmbedAndStoreAsync(Note note)
    {
        var text = BuildEmbeddingText(note);
        var vector = await EmbedAsync(text);
        if (vector is not null)
        {
            _store[note.VaultRelativePath] = new EmbeddingEntry(note.VaultRelativePath, note.ContentHash, vector);
        }
    }

    private static string BuildEmbeddingText(Note note)
    {
        var sb = new StringBuilder();
        sb.AppendLine(note.Name);

        var m = note.Metadata;
        if (m.Tags.Count > 0)
        {
            sb.AppendLine($"Tags: {string.Join(", ", m.Tags)}");
        }

        if (m.Aliases.Count > 0)
        {
            sb.AppendLine($"Aliases: {string.Join(", ", m.Aliases)}");
        }

        if (m.Status is not null)
        {
            sb.AppendLine($"Status: {m.Status}");
        }

        if (m.NoteType is not null)
        {
            sb.AppendLine($"Type: {m.NoteType}");
        }

        if (m.Domain is not null)
        {
            sb.AppendLine($"Domain: {m.Domain}");
        }

        if (m.Date.HasValue)
        {
            sb.AppendLine($"Date: {m.Date:yyyy-MM-dd}");
        }

        if (m.Updated.HasValue)
        {
            sb.AppendLine($"Updated: {m.Updated:yyyy-MM-dd}");
        }

        foreach (var (k, v) in m.ExtraFields)
        {
            sb.AppendLine($"{k}: {v}");
        }

        if (!string.IsNullOrWhiteSpace(note.PlainText))
        {
            sb.AppendLine();
            sb.Append(note.PlainText);
        }

        return sb.ToString();
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

internal record EmbeddingEntry(string VaultRelativePath, string Hash, float[] Vector);

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
