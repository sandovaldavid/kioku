using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// A golden set of retrieval-quality queries: each query lists the vault-relative paths
/// of the notes that a good search should return, with a graded relevance (1-3).
/// Queries with an empty relevant list are deliberate "no relevant answer" probes.
/// Loaded from JSON (see src/Kioku.Mcp.Server.Tests/Fixtures/golden-set.json).
/// </summary>
public sealed class GoldenSet
{
    [JsonPropertyName("queries")]
    public required IReadOnlyList<GoldenQuery> Queries { get; init; }

    public static GoldenSet Load(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var set = JsonSerializer.Deserialize(stream, GoldenSetJsonContext.Default.GoldenSet);
        return set ?? throw new InvalidDataException($"Golden set file is empty or invalid: {filePath}");
    }
}

public sealed class GoldenQuery
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("query")]
    public required string Query { get; init; }

    [JsonPropertyName("relevant")]
    public IReadOnlyList<RelevantNote> Relevant { get; init; } = [];

    /// <summary>True when this query is a "no relevant answer" precision probe.</summary>
    [JsonIgnore]
    public bool HasRelevantNotes => Relevant.Count > 0;

    /// <summary>
    /// Relevance judgments keyed by normalized vault-relative path ('/' separators).
    /// A missing or non-positive grade counts as 1 (relevant).
    /// </summary>
    public IReadOnlyDictionary<string, int> RelevanceByPath() =>
        Relevant.ToDictionary(
            r => r.Path.Replace('\\', '/').Trim().TrimStart('/'),
            r => r.Grade > 0 ? r.Grade : 1,
            StringComparer.OrdinalIgnoreCase);
}

public sealed class RelevantNote
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("grade")]
    public int Grade { get; init; } = 1;
}

[JsonSerializable(typeof(GoldenSet))]
internal partial class GoldenSetJsonContext : JsonSerializerContext
{
}
