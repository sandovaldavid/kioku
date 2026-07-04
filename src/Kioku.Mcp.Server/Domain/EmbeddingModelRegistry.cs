namespace Kioku.Mcp.Server.Domain;

/// <summary>
/// Registry of known Ollama embedding models and their expected output dimensions.
/// </summary>
public static class EmbeddingModelRegistry
{
    private static readonly Dictionary<string, int> KnownDimensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nomic-embed-text"] = 768,
        ["nomic-embed-text-v1"] = 768,
        ["nomic-embed-text-v1.5"] = 768,
        ["mxbai-embed-large"] = 1024,
        ["bge-m3"] = 1024,
        ["bge-large-en-v1.5"] = 1024,
        ["all-minilm"] = 384,
        ["snowflake-arctic-embed"] = 768,
        ["snowflake-arctic-embed-s"] = 384,
        ["snowflake-arctic-embed-m"] = 768,
        ["snowflake-arctic-embed-l"] = 1024,
        ["gte-small"] = 384,
        ["gte-base"] = 768,
        ["gte-large"] = 1024,
        ["jina-embeddings-v2-base-en"] = 768,
        ["jina-embeddings-v2-small-en"] = 512,
    };

    /// <summary>
    /// Default dimension used when a model is not in the registry.
    /// </summary>
    public const int DefaultDimension = 768;

    /// <summary>
    /// Returns the expected dimension for a model, or the default if unknown.
    /// </summary>
    public static int GetExpectedDimension(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            return DefaultDimension;
        }

        return KnownDimensions.TryGetValue(modelName, out var dim) ? dim : DefaultDimension;
    }

    /// <summary>
    /// Returns true if the model is in the known registry.
    /// </summary>
    public static bool IsKnownModel(string modelName)
    {
        return !string.IsNullOrWhiteSpace(modelName) && KnownDimensions.ContainsKey(modelName);
    }

    /// <summary>
    /// Returns all known model names.
    /// </summary>
    public static IReadOnlyCollection<string> KnownModels => KnownDimensions.Keys;
}
