using System.Net;

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
    /// Transport mode: "stdio" (default) or "http" (Streamable HTTP).
    /// Environment variable: KIOKU_TRANSPORT
    /// </summary>
    public string Transport { get; init; } = "stdio";

    /// <summary>
    /// HTTP port for the Streamable HTTP transport.
    /// Default: 5173. Environment variable: KIOKU_HTTP_PORT
    /// </summary>
    public int HttpPort { get; init; } = 5173;

    /// <summary>
    /// Interface or host name used by the Streamable HTTP listener.
    /// Defaults to the IPv4 loopback address. Environment variable: KIOKU_HTTP_HOST
    /// </summary>
    public string HttpHost { get; init; } = "127.0.0.1";

    /// <summary>
    /// Bearer token for API key authentication (Streamable HTTP only).
    /// A token is required for non-loopback bindings unless the insecure override is enabled.
    /// Environment variable: KIOKU_API_KEY
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Explicit Origin values accepted by the Streamable HTTP endpoint. Missing Origin headers
    /// remain valid for non-browser MCP clients. Environment variable: KIOKU_HTTP_ALLOWED_ORIGINS
    /// (comma-separated).
    /// </summary>
    public IReadOnlyList<string> HttpAllowedOrigins { get; init; } =
        ["http://localhost", "http://127.0.0.1", "http://[::1]", "app://obsidian.md"];

    /// <summary>
    /// Exact proxy IP addresses trusted to supply X-Forwarded-For and X-Forwarded-Proto.
    /// Forwarded headers are disabled when this list is empty. Environment variable:
    /// KIOKU_HTTP_TRUSTED_PROXIES (comma-separated).
    /// </summary>
    public IReadOnlyList<string> HttpTrustedProxies { get; init; } = [];

    /// <summary>
    /// Explicitly permits an unauthenticated non-loopback listener. This is unsafe and is
    /// disabled by default. Environment variable: KIOKU_ALLOW_INSECURE_HTTP
    /// </summary>
    public bool AllowInsecureHttp { get; init; }

    /// <summary>
    /// Maximum request body accepted by Kestrel. Default: 1 MiB.
    /// Environment variable: KIOKU_HTTP_MAX_REQUEST_BODY_BYTES
    /// </summary>
    public long HttpMaxRequestBodyBytes { get; init; } = 1024 * 1024;

    /// <summary>
    /// Maximum execution time for an MCP POST request. Streamable GET connections are not
    /// subject to this timeout. Default: 300 seconds.
    /// Environment variable: KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS
    /// </summary>
    public int HttpRequestTimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// Enables lightweight, in-memory tool-call telemetry.
    /// When enabled, Kioku counts tool invocations (never note contents).
    /// Default: false. Environment variable: KIOKU_ENABLE_METRICS
    /// </summary>
    public bool EnableMetrics { get; init; }

    /// <summary>
    /// Enables W3C-compatible coordination activities for an explicitly configured listener.
    /// No exporter is configured by Kioku. Default: false. Environment variable:
    /// KIOKU_ENABLE_TRACING
    /// </summary>
    public bool EnableTracing { get; init; }

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
    /// Returns true when the server is running in Streamable HTTP transport mode.
    /// </summary>
    public bool IsHttpTransport => Transport.Equals("http", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns true when the configured listener is restricted to loopback.</summary>
    public bool IsLoopbackHttpBinding => IsLoopbackHost(HttpHost);

    /// <summary>Returns true when a non-empty HTTP bearer token is configured.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>URL used by Kestrel for the Streamable HTTP listener.</summary>
    public string HttpListenUrl
    {
        get
        {
            var host = IPAddress.TryParse(HttpHost, out var address) &&
                address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                    ? $"[{HttpHost}]"
                    : HttpHost;
            return $"http://{host}:{HttpPort}";
        }
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

}
