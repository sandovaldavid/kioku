using System.Net;
using Kioku.Mcp.Server.Http;

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

        var httpPort = ReadInt("KIOKU_HTTP_PORT", 5173);
        var httpHost = Environment.GetEnvironmentVariable("KIOKU_HTTP_HOST") ?? "127.0.0.1";

        var apiKey = Environment.GetEnvironmentVariable("KIOKU_API_KEY");
        var httpAllowedOrigins = SplitCommaSeparated(
            Environment.GetEnvironmentVariable("KIOKU_HTTP_ALLOWED_ORIGINS"),
            ["http://localhost", "http://127.0.0.1", "http://[::1]", "app://obsidian.md"]);
        var httpTrustedProxies = SplitCommaSeparated(
            Environment.GetEnvironmentVariable("KIOKU_HTTP_TRUSTED_PROXIES"), []);
        var allowInsecureHttp = bool.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_ALLOW_INSECURE_HTTP"), out var aih) && aih;
        var httpMaxRequestBodyBytes = ReadLong(
            "KIOKU_HTTP_MAX_REQUEST_BODY_BYTES", 1024 * 1024);
        var httpRequestTimeoutSeconds = ReadInt(
            "KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS", 300);
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
            HttpHost = httpHost,
            ApiKey = apiKey,
            HttpAllowedOrigins = httpAllowedOrigins,
            HttpTrustedProxies = httpTrustedProxies,
            AllowInsecureHttp = allowInsecureHttp,
            HttpMaxRequestBodyBytes = httpMaxRequestBodyBytes,
            HttpRequestTimeoutSeconds = httpRequestTimeoutSeconds,
            GitHubToken = githubToken,
            EnableMetrics = enableMetrics,
            SentryDsn = sentryDsn,
            AllowExternalReads = allowExternalReads,
            ExternalReadRoots = externalReadRoots,
            AllowPermanentDelete = allowPermanentDelete,
        };
    }

    /// <summary>Validates security-sensitive Streamable HTTP settings before binding.</summary>
    public void ValidateHttpTransport()
    {
        if (HttpPort is < 1 or > 65535)
        {
            throw new InvalidOperationException("KIOKU_HTTP_PORT must be between 1 and 65535.");
        }

        if (!IsValidHttpHost(HttpHost))
        {
            throw new InvalidOperationException(
                "KIOKU_HTTP_HOST must be a host name, IP address, '*', or '+', without a URL scheme or path.");
        }

        if (HttpMaxRequestBodyBytes is < 1024 or > 100 * 1024 * 1024)
        {
            throw new InvalidOperationException(
                "KIOKU_HTTP_MAX_REQUEST_BODY_BYTES must be between 1024 and 104857600 bytes.");
        }

        if (HttpRequestTimeoutSeconds is < 1 or > 3600)
        {
            throw new InvalidOperationException(
                "KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS must be between 1 and 3600 seconds.");
        }

        foreach (var origin in HttpAllowedOrigins)
        {
            if (!HttpOrigin.TryNormalize(origin, out _))
            {
                throw new InvalidOperationException(
                    $"KIOKU_HTTP_ALLOWED_ORIGINS contains an invalid origin: '{origin}'.");
            }
        }

        foreach (var proxy in HttpTrustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                throw new InvalidOperationException(
                    $"KIOKU_HTTP_TRUSTED_PROXIES contains an invalid IP address: '{proxy}'.");
            }
        }

        if (!IsLoopbackHttpBinding && !HasApiKey && !AllowInsecureHttp)
        {
            throw new InvalidOperationException(
                "Refusing an unauthenticated non-loopback Streamable HTTP listener. " +
                "Configure KIOKU_API_KEY or explicitly set KIOKU_ALLOW_INSECURE_HTTP=true.");
        }
    }

    private static bool IsLoopbackHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }

    private static bool IsValidHttpHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Contains("//", StringComparison.Ordinal) ||
            host.Contains('/') || host.Contains('\\'))
        {
            return false;
        }

        return host is "*" or "+" ||
            IPAddress.TryParse(host, out _) ||
            Uri.CheckHostName(host) is UriHostNameType.Dns;
    }

    private static int ReadInt(string variable, int defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{variable} must be an integer.");
    }

    private static long ReadLong(string variable, long defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return long.TryParse(value, out var parsed)
            ? parsed
            : throw new InvalidOperationException($"{variable} must be an integer.");
    }

    private static IReadOnlyList<string> SplitCommaSeparated(
        string? value,
        IReadOnlyList<string> defaultValue) =>
        string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
