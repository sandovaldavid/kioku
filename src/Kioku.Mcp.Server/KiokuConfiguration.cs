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
    /// Shared secret required to authenticate with the Obsidian bridge WebSocket.
    /// Must match the "Auth token" setting in the Kioku Obsidian plugin.
    /// If null or empty, the bridge handshake is a no-op (matches pre-auth behavior).
    /// Environment variable: KIOKU_BRIDGE_TOKEN
    /// </summary>
    public string? BridgeToken { get; init; }

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
    /// Ollama model used for local text generation (summarize_note, etc.), e.g. "llama3.2".
    /// If null or empty, local generation is disabled and its tools report
    /// [error] [DEPENDENCY_UNAVAILABLE] with setup instructions.
    /// Environment variable: KIOKU_GEN_MODEL
    /// </summary>
    public string? GenerationModel { get; init; }

    /// <summary>
    /// Transport mode: "stdio" (default, v1) or "http" (v2 HTTP-SSE).
    /// Environment variable: KIOKU_TRANSPORT
    /// </summary>
    public string Transport { get; init; } = "stdio";

    /// <summary>
    /// HTTP port for the HTTP-SSE transport (v2 only).
    /// Default: 5173. Environment variable: KIOKU_HTTP_PORT
    /// </summary>
    public int HttpPort { get; init; } = 5173;

    /// <summary>
    /// Bearer token for API key authentication (v2 HTTP only).
    /// If null or empty, the endpoint is unprotected — use only in trusted local networks.
    /// Environment variable: KIOKU_API_KEY
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// GitHub personal access token for Gist sharing.
    /// Requires the 'gist' scope. Optional.
    /// Environment variable: KIOKU_GITHUB_TOKEN
    /// </summary>
    public string? GitHubToken { get; init; }

    /// <summary>
    /// Enables lightweight, in-memory tool-call telemetry.
    /// When enabled, Kioku counts tool invocations (never note contents).
    /// Default: false. Environment variable: KIOKU_ENABLE_METRICS
    /// </summary>
    public bool EnableMetrics { get; init; }

    /// <summary>
    /// Sentry DSN for opt-in crash reporting.
    /// If null or empty, crash reporting is disabled.
    /// Environment variable: KIOKU_SENTRY_DSN
    /// </summary>
    public string? SentryDsn { get; init; }

    /// <summary>
    /// Enables reads outside the vault only when the canonical source is also under one of
    /// <see cref="ExternalReadRoots"/>. Default: false.
    /// Environment variable: KIOKU_ALLOW_EXTERNAL_READS
    /// </summary>
    public bool AllowExternalReads { get; init; }

    /// <summary>
    /// Explicit roots allowed for external read-only imports. Entries are separated with the
    /// platform path separator (';' on Windows, ':' on Unix).
    /// Environment variable: KIOKU_EXTERNAL_READ_ROOTS
    /// </summary>
    public IReadOnlyList<string> ExternalReadRoots { get; init; } = [];

    /// <summary>
    /// Enables irreversible file deletion. Soft-delete remains available when disabled.
    /// Default: false. Environment variable: KIOKU_ALLOW_PERMANENT_DELETE
    /// </summary>
    public bool AllowPermanentDelete { get; init; }

    /// <summary>
    /// Returns true when the server is running in HTTP-SSE transport mode.
    /// </summary>
    public bool IsHttpTransport => Transport.Equals("http", StringComparison.OrdinalIgnoreCase);

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

        var bridgeToken = Environment.GetEnvironmentVariable("KIOKU_BRIDGE_TOKEN");

        var ollamaUrl = Environment.GetEnvironmentVariable("KIOKU_OLLAMA_URL")
            ?? "http://localhost:11434";

        var embeddingModel = Environment.GetEnvironmentVariable("KIOKU_EMBEDDING_MODEL")
            ?? "nomic-embed-text";

        var generationModel = Environment.GetEnvironmentVariable("KIOKU_GEN_MODEL");

        var transport = Environment.GetEnvironmentVariable("KIOKU_TRANSPORT")
            ?? "stdio";

        var httpPort = int.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_HTTP_PORT"), out var hp) ? hp : 5173;

        var apiKey = Environment.GetEnvironmentVariable("KIOKU_API_KEY");
        var githubToken = Environment.GetEnvironmentVariable("KIOKU_GITHUB_TOKEN");
        var enableMetrics = bool.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_ENABLE_METRICS"), out var em) && em;
        var sentryDsn = Environment.GetEnvironmentVariable("KIOKU_SENTRY_DSN");
        var allowExternalReads = bool.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_ALLOW_EXTERNAL_READS"), out var aer) && aer;
        var externalReadRoots = (Environment.GetEnvironmentVariable("KIOKU_EXTERNAL_READ_ROOTS") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Path.GetFullPath)
            .ToArray();
        var allowPermanentDelete = bool.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_ALLOW_PERMANENT_DELETE"), out var apd) && apd;

        return new KiokuConfiguration
        {
            VaultPath = Path.GetFullPath(vaultPath),
            MaxSearchResults = maxResults,
            ObsidianBridgePort = port,
            BridgeToken = bridgeToken,
            OllamaUrl = ollamaUrl,
            EmbeddingModel = embeddingModel,
            GenerationModel = generationModel,
            Transport = transport,
            HttpPort = httpPort,
            ApiKey = apiKey,
            GitHubToken = githubToken,
            EnableMetrics = enableMetrics,
            SentryDsn = sentryDsn,
            AllowExternalReads = allowExternalReads,
            ExternalReadRoots = externalReadRoots,
            AllowPermanentDelete = allowPermanentDelete,
        };
    }
}
