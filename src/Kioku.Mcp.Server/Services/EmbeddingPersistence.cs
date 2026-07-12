using System.Text;

namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Binary serialization for the embedding cache file.
///
/// File header:
///   magic "KIOKU_EMB\n" (10 bytes)
///   uint32 formatVersion
///   uint16 textSchemeVersion (how the embedded text was built; bump to force re-embedding
///                             when the text construction changes without a format change)
///   uint16 modelNameLen + byte[modelNameLen] UTF-8 model name
///   uint16 dimension
///   uint32 count
///
/// Format per entry:
///   uint16  pathLen  + byte[pathLen]  UTF-8 vault-relative path
///   uint16  hashLen  + byte[hashLen]  UTF-8 MD5 hex content hash
///   uint16  dim      + float[dim]     IEEE 754 LE embedding vector
///
/// The cache is invalidated if the format, text scheme, model name or dimension changes.
/// </summary>
internal static class EmbeddingPersistence
{
    private static readonly byte[] Magic = "KIOKU_EMB\n"u8.ToArray();
    private const uint FormatVersion = 4;

    // Scheme 1: note-level text with the model's document task prefix applied.
    private const ushort TextSchemeVersion = 1;

    public static async Task SaveAsync(
        string filePath,
        IReadOnlyDictionary<string, EmbeddingEntry> store,
        string modelName,
        int dimension)
    {
        var dir = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(dir);

        var tmp = filePath + ".tmp";
        await using var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None,
            bufferSize: 65536, useAsync: true);
        await using var writer = new BinaryWriter(fs, Encoding.UTF8, leaveOpen: true);

        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(TextSchemeVersion);

        var modelBytes = Encoding.UTF8.GetBytes(modelName);
        writer.Write((ushort)modelBytes.Length);
        writer.Write(modelBytes);
        writer.Write((ushort)dimension);

        writer.Write((uint)store.Count);

        foreach (var (_, entry) in store)
        {
            var pathBytes = Encoding.UTF8.GetBytes(entry.VaultRelativePath);
            var hashBytes = Encoding.UTF8.GetBytes(entry.Hash);

            writer.Write((ushort)pathBytes.Length);
            writer.Write(pathBytes);
            writer.Write((ushort)hashBytes.Length);
            writer.Write(hashBytes);
            writer.Write((ushort)entry.Vector.Length);

            foreach (var f in entry.Vector)
            {
                writer.Write(f);
            }
        }

        writer.Flush();
        fs.Close();

        File.Move(tmp, filePath, overwrite: true);
    }

    public static async Task<(Dictionary<string, EmbeddingEntry> Entries, string? ModelName, int Dimension)> LoadAsync(string filePath)
    {
        var result = new Dictionary<string, EmbeddingEntry>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(filePath))
        {
            return (result, null, 0);
        }

        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 65536, useAsync: true);
        using var reader = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        // Validate magic
        var magic = reader.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic))
        {
            return (result, null, 0);
        }

        var version = reader.ReadUInt32();
        if (version != FormatVersion)
        {
            return (result, null, 0);
        }

        var textScheme = reader.ReadUInt16();
        if (textScheme != TextSchemeVersion)
        {
            return (result, null, 0);
        }

        // Read model name
        var modelNameLen = reader.ReadUInt16();
        var modelName = Encoding.UTF8.GetString(reader.ReadBytes(modelNameLen));

        // Read dimension
        var dimension = reader.ReadUInt16();

        var count = reader.ReadUInt32();
        result.EnsureCapacity((int)count);

        for (uint i = 0; i < count; i++)
        {
            var pathLen = reader.ReadUInt16();
            var path = Encoding.UTF8.GetString(reader.ReadBytes(pathLen));

            var hashLen = reader.ReadUInt16();
            var hash = Encoding.UTF8.GetString(reader.ReadBytes(hashLen));

            var dim = reader.ReadUInt16();
            var vector = new float[dim];
            for (int j = 0; j < dim; j++)
            {
                vector[j] = reader.ReadSingle();
            }

            result[path] = new EmbeddingEntry(path, hash, vector);
        }

        return (result, modelName, dimension);
    }
}
