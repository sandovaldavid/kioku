namespace Kioku.Mcp.Server;

/// <summary>
/// Configuration for Kioku MCP Server.
/// Read from environment variables or command-line arguments.
/// </summary>
public sealed class KiokuConfiguration
{
    /// <summary>
    /// Absolute path to the root of the Obsidian vault.
    /// Environment variable: KIOKU_VAULT_PATH
    /// </summary>
    public required string VaultPath { get; init; }

    /// <summary>
    /// Maximum number of search results to return.
    /// Default: 20. Environment variable: KIOKU_MAX_RESULTS
    /// </summary>
    public int MaxSearchResults { get; init; } = 20;

    /// <summary>
    /// WebSocket port of the Obsidian plugin.
    /// Default: 7765. Environment variable: KIOKU_OBSIDIAN_PORT
    /// </summary>
    public int ObsidianBridgePort { get; init; } = 7765;

    /// <summary>
    /// Base URL of the Ollama server for embedding generation.
    /// Default: http://localhost:11434. Environment variable: KIOKU_OLLAMA_URL
    /// </summary>
    public string OllamaUrl { get; init; } = "http://localhost:11434";

    /// <summary>
    /// Ollama embedding model to use.
    /// Default: nomic-embed-text. Environment variable: KIOKU_EMBEDDING_MODEL
    /// </summary>
    public string EmbeddingModel { get; init; } = "nomic-embed-text";

    /// <summary>
    /// Loads the configuration from environment variables.
    /// </summary>
    public static KiokuConfiguration FromEnvironment()
    {
        var vaultPath = Environment.GetEnvironmentVariable("KIOKU_VAULT_PATH")
            ?? throw new InvalidOperationException(
                "The KIOKU_VAULT_PATH environment variable is not configured. " +
                "Set KIOKU_VAULT_PATH with the path to your Obsidian vault.\n" +
                "Example: export KIOKU_VAULT_PATH=\"/home/user/my-vault\"");

        var maxResults = int.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_MAX_RESULTS"), out var r) ? r : 20;

        var port = int.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_OBSIDIAN_PORT"), out var p) ? p : 7765;

        var ollamaUrl = Environment.GetEnvironmentVariable("KIOKU_OLLAMA_URL")
            ?? "http://localhost:11434";

        var embeddingModel = Environment.GetEnvironmentVariable("KIOKU_EMBEDDING_MODEL")
            ?? "nomic-embed-text";

        return new KiokuConfiguration
        {
            VaultPath = Path.GetFullPath(vaultPath),
            MaxSearchResults = maxResults,
            ObsidianBridgePort = port,
            OllamaUrl = ollamaUrl,
            EmbeddingModel = embeddingModel,
        };
    }
}
