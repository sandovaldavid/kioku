using Kioku.Mcp.Server.Services;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Round-trip tests for the embedding cache's binary format (v5: multiple heading-tagged
/// chunks per note, replacing the single-vector-per-note v4 layout), plus the
/// version-mismatch-discards-cache invariant that guards every format/scheme bump.
/// </summary>
public class EmbeddingPersistenceTests : IAsyncLifetime
{
    private string _cachePath = null!;

    public Task InitializeAsync()
    {
        _cachePath = Path.Combine(Path.GetTempPath(), $"kioku-persist-test-{Guid.NewGuid():N}", "embeddings.bin");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_cachePath)!, recursive: true);
        }
        catch
        {
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task SaveAndLoad_MultiChunkEntry_RoundTripsHeadingPathsAndVectors()
    {
        var store = new Dictionary<string, EmbeddingEntry>
        {
            ["Long Note.md"] = new EmbeddingEntry(
                "Long Note.md",
                "hash-1",
                [
                    new EmbeddingChunk("Long Note > Intro", [0.1f, 0.2f, 0.3f]),
                    new EmbeddingChunk("Long Note > Details > Sub", [0.4f, 0.5f, 0.6f]),
                ]),
            ["Short Note.md"] = new EmbeddingEntry(
                "Short Note.md",
                "hash-2",
                [new EmbeddingChunk("", [0.7f, 0.8f, 0.9f])]),
        };

        await EmbeddingPersistence.SaveAsync(_cachePath, store, "nomic-embed-text", dimension: 3);
        var (loaded, modelName, dimension) = await EmbeddingPersistence.LoadAsync(_cachePath);

        Assert.Equal("nomic-embed-text", modelName);
        Assert.Equal(3, dimension);
        Assert.Equal(2, loaded.Count);

        var longNote = loaded["Long Note.md"];
        Assert.Equal("hash-1", longNote.Hash);
        Assert.Equal(2, longNote.Chunks.Count);
        Assert.Equal("Long Note > Intro", longNote.Chunks[0].HeadingPath);
        Assert.Equal([0.1f, 0.2f, 0.3f], longNote.Chunks[0].Vector);
        Assert.Equal("Long Note > Details > Sub", longNote.Chunks[1].HeadingPath);
        Assert.Equal([0.4f, 0.5f, 0.6f], longNote.Chunks[1].Vector);

        var shortNote = loaded["Short Note.md"];
        Assert.Single(shortNote.Chunks);
        Assert.Equal("", shortNote.Chunks[0].HeadingPath);
        Assert.Equal([0.7f, 0.8f, 0.9f], shortNote.Chunks[0].Vector);
    }

    [Fact]
    public async Task Load_NoFile_ReturnsEmptyStore()
    {
        var (loaded, modelName, dimension) = await EmbeddingPersistence.LoadAsync(_cachePath);

        Assert.Empty(loaded);
        Assert.Null(modelName);
        Assert.Equal(0, dimension);
    }

    [Fact]
    public async Task Load_OldFormatVersion_DiscardsCache()
    {
        var store = new Dictionary<string, EmbeddingEntry>
        {
            ["Note.md"] = new EmbeddingEntry("Note.md", "hash", [new EmbeddingChunk("", [1f, 2f])]),
        };
        await EmbeddingPersistence.SaveAsync(_cachePath, store, "nomic-embed-text", dimension: 2);

        // Overwrite just the 4-byte formatVersion field (right after the 10-byte magic) with
        // a stale value, simulating a cache written by a pre-chunking build.
        var bytes = await File.ReadAllBytesAsync(_cachePath);
        BitConverter.TryWriteBytes(bytes.AsSpan(10, 4), 4u);
        await File.WriteAllBytesAsync(_cachePath, bytes);

        var (loaded, modelName, _) = await EmbeddingPersistence.LoadAsync(_cachePath);

        Assert.Empty(loaded);
        Assert.Null(modelName);
    }
}
