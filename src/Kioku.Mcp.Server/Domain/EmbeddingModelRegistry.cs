namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Metadata for a known Ollama embedding model: output dimension and the task-instruction
/// prefixes the model was trained with. Models like nomic-embed-text require asymmetric
/// prefixes ("search_document: " when indexing, "search_query: " when querying) to reach
/// their advertised retrieval quality; symmetric models need none.
/// </summary>
public sealed record EmbeddingModelInfo(int Dimension, string? DocumentPrefix = null, string? QueryPrefix = null);

/// <summary>
/// Registry of known Ollama embedding models, their expected output dimensions and
/// task-instruction prefixes.
/// </summary>
public static class EmbeddingModelRegistry
{
    private const string NomicDocumentPrefix = "search_document: ";
    private const string NomicQueryPrefix = "search_query: ";
    private const string MxbaiQueryPrefix = "Represent this sentence for searching relevant passages: ";

    private static readonly Dictionary<string, EmbeddingModelInfo> Registry = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nomic-embed-text"] = new(768, NomicDocumentPrefix, NomicQueryPrefix),
        ["nomic-embed-text-v1"] = new(768, NomicDocumentPrefix, NomicQueryPrefix),
        ["nomic-embed-text-v1.5"] = new(768, NomicDocumentPrefix, NomicQueryPrefix),
        ["mxbai-embed-large"] = new(1024, QueryPrefix: MxbaiQueryPrefix),
        ["bge-m3"] = new(1024),
        ["bge-large-en-v1.5"] = new(1024),
        ["all-minilm"] = new(384),
        ["snowflake-arctic-embed"] = new(768),
        ["snowflake-arctic-embed-s"] = new(384),
        ["snowflake-arctic-embed-m"] = new(768),
        ["snowflake-arctic-embed-l"] = new(1024),
        ["gte-small"] = new(384),
        ["gte-base"] = new(768),
        ["gte-large"] = new(1024),
        ["jina-embeddings-v2-base-en"] = new(768),
        ["jina-embeddings-v2-small-en"] = new(512),
    };

    /// <summary>
    /// Default dimension used when a model is not in the registry.
    /// </summary>
    public const int DefaultDimension = 768;

    private static readonly EmbeddingModelInfo DefaultInfo = new(DefaultDimension);

    /// <summary>
    /// Returns the metadata for a model, or a prefix-less default entry if unknown.
    /// </summary>
    public static EmbeddingModelInfo GetModelInfo(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return DefaultInfo;
        }

        return Registry.TryGetValue(modelName, out var info) ? info : DefaultInfo;
    }

    /// <summary>
    /// Returns the expected dimension for a model, or the default if unknown.
    /// </summary>
    public static int GetExpectedDimension(string modelName) => GetModelInfo(modelName).Dimension;

    /// <summary>
    /// Returns true if the model is in the known registry.
    /// </summary>
    public static bool IsKnownModel(string modelName)
    {
        return !string.IsNullOrWhiteSpace(modelName) && Registry.ContainsKey(modelName);
    }

    /// <summary>
    /// Returns all known model names.
    /// </summary>
    public static IReadOnlyCollection<string> KnownModels => Registry.Keys;
}
