using System.Net;
using Kioku.Mcp.Server.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Kioku.Mcp.Server.Hosting;

/// <summary>
/// Bindable server options. The <c>Kioku</c> configuration section is the canonical source;
/// legacy KIOKU_* environment variables are projected into this section for compatibility.
/// </summary>
public sealed class KiokuOptions
{
    public const string SectionName = "Kioku";

    public string VaultPath { get; set; } = string.Empty;
    public int MaxSearchResults { get; set; } = 20;
    public int ObsidianBridgePort { get; set; } = 7765;
    public string? BridgeToken { get; set; }
    public string OllamaUrl { get; set; } = "http://localhost:11434";
    public string EmbeddingModel { get; set; } = "nomic-embed-text";
    public string? GenerationModel { get; set; }
    public string Transport { get; set; } = "stdio";
    public int HttpPort { get; set; } = 5173;
    public string HttpHost { get; set; } = "127.0.0.1";
    public string? ApiKey { get; set; }
    public string[] HttpAllowedOrigins { get; set; } =
        ["http://localhost", "http://127.0.0.1", "http://[::1]", "app://obsidian.md"];
    public string[] HttpTrustedProxies { get; set; } = [];
    public bool AllowInsecureHttp { get; set; }
    public long HttpMaxRequestBodyBytes { get; set; } = 1024 * 1024;
    public int HttpRequestTimeoutSeconds { get; set; } = 300;
    public string? GitHubToken { get; set; }
    public bool EnableMetrics { get; set; }
    public string? SentryDsn { get; set; }
    public bool AllowExternalReads { get; set; }
    public string[] ExternalReadRoots { get; set; } = [];
    public bool AllowPermanentDelete { get; set; }
    public int IndexConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public int EmbeddingConcurrency { get; set; } = 2;

    public bool IsHttpTransport => Transport.Equals("http", StringComparison.OrdinalIgnoreCase);

    public KiokuConfiguration ToConfiguration() => new()
    {
        VaultPath = Path.GetFullPath(VaultPath),
        MaxSearchResults = MaxSearchResults,
        ObsidianBridgePort = ObsidianBridgePort,
        BridgeToken = BridgeToken,
        OllamaUrl = OllamaUrl,
        EmbeddingModel = EmbeddingModel,
        GenerationModel = GenerationModel,
        Transport = Transport,
        HttpPort = HttpPort,
        HttpHost = HttpHost,
        ApiKey = ApiKey,
        HttpAllowedOrigins = HttpAllowedOrigins,
        HttpTrustedProxies = HttpTrustedProxies,
        AllowInsecureHttp = AllowInsecureHttp,
        HttpMaxRequestBodyBytes = HttpMaxRequestBodyBytes,
        HttpRequestTimeoutSeconds = HttpRequestTimeoutSeconds,
        GitHubToken = GitHubToken,
        EnableMetrics = EnableMetrics,
        SentryDsn = SentryDsn,
        AllowExternalReads = AllowExternalReads,
        ExternalReadRoots = ExternalReadRoots.Select(Path.GetFullPath).ToArray(),
        AllowPermanentDelete = AllowPermanentDelete,
    };
}

internal static class KiokuOptionsConfiguration
{
    private static readonly IReadOnlyDictionary<string, string> ScalarEnvironmentMappings =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KIOKU_VAULT_PATH"] = nameof(KiokuOptions.VaultPath),
            ["KIOKU_MAX_RESULTS"] = nameof(KiokuOptions.MaxSearchResults),
            ["KIOKU_OBSIDIAN_PORT"] = nameof(KiokuOptions.ObsidianBridgePort),
            ["KIOKU_BRIDGE_TOKEN"] = nameof(KiokuOptions.BridgeToken),
            ["KIOKU_OLLAMA_URL"] = nameof(KiokuOptions.OllamaUrl),
            ["KIOKU_EMBEDDING_MODEL"] = nameof(KiokuOptions.EmbeddingModel),
            ["KIOKU_GEN_MODEL"] = nameof(KiokuOptions.GenerationModel),
            ["KIOKU_TRANSPORT"] = nameof(KiokuOptions.Transport),
            ["KIOKU_HTTP_PORT"] = nameof(KiokuOptions.HttpPort),
            ["KIOKU_HTTP_HOST"] = nameof(KiokuOptions.HttpHost),
            ["KIOKU_API_KEY"] = nameof(KiokuOptions.ApiKey),
            ["KIOKU_ALLOW_INSECURE_HTTP"] = nameof(KiokuOptions.AllowInsecureHttp),
            ["KIOKU_HTTP_MAX_REQUEST_BODY_BYTES"] = nameof(KiokuOptions.HttpMaxRequestBodyBytes),
            ["KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS"] = nameof(KiokuOptions.HttpRequestTimeoutSeconds),
            ["KIOKU_GITHUB_TOKEN"] = nameof(KiokuOptions.GitHubToken),
            ["KIOKU_ENABLE_METRICS"] = nameof(KiokuOptions.EnableMetrics),
            ["KIOKU_SENTRY_DSN"] = nameof(KiokuOptions.SentryDsn),
            ["KIOKU_ALLOW_EXTERNAL_READS"] = nameof(KiokuOptions.AllowExternalReads),
            ["KIOKU_ALLOW_PERMANENT_DELETE"] = nameof(KiokuOptions.AllowPermanentDelete),
            ["KIOKU_INDEX_CONCURRENCY"] = nameof(KiokuOptions.IndexConcurrency),
            ["KIOKU_EMBEDDING_CONCURRENCY"] = nameof(KiokuOptions.EmbeddingConcurrency),
        };

    internal static IConfiguration Build(string[] args)
    {
        var configuration = new ConfigurationManager();
        configuration
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
        configuration.AddInMemoryCollection(GetLegacyValues(Environment.GetEnvironmentVariable));
        configuration.AddCommandLine(args);

        if (args.Contains("--http", StringComparer.OrdinalIgnoreCase))
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{KiokuOptions.SectionName}:{nameof(KiokuOptions.Transport)}"] = "http",
            });
        }

        return configuration;
    }

    internal static KiokuOptions GetValidated(IConfiguration configuration)
    {
        var options = configuration.GetSection(KiokuOptions.SectionName).Get<KiokuOptions>() ?? new KiokuOptions();
        var validation = new KiokuOptionsValidator().Validate(Options.DefaultName, options);
        if (validation.Failed)
        {
            throw new OptionsValidationException(
                Options.DefaultName,
                typeof(KiokuOptions),
                validation.Failures);
        }

        return options;
    }

    internal static IReadOnlyDictionary<string, string?> GetLegacyValues(Func<string, string?> read)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (environmentName, propertyName) in ScalarEnvironmentMappings)
        {
            var value = read(environmentName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[$"{KiokuOptions.SectionName}:{propertyName}"] = value;
            }
        }

        AddList(values, nameof(KiokuOptions.HttpAllowedOrigins), read("KIOKU_HTTP_ALLOWED_ORIGINS"), ',');
        AddList(values, nameof(KiokuOptions.HttpTrustedProxies), read("KIOKU_HTTP_TRUSTED_PROXIES"), ',');
        AddList(values, nameof(KiokuOptions.ExternalReadRoots), read("KIOKU_EXTERNAL_READ_ROOTS"), Path.PathSeparator);
        return values;
    }

    private static void AddList(
        IDictionary<string, string?> values,
        string propertyName,
        string? raw,
        char separator)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var items = raw.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 0; index < items.Length; index++)
        {
            values[$"{KiokuOptions.SectionName}:{propertyName}:{index}"] = items[index];
        }
    }
}

public sealed class KiokuOptionsValidator : IValidateOptions<KiokuOptions>
{
    public ValidateOptionsResult Validate(string? name, KiokuOptions options)
    {
        var failures = new List<string>();

        ValidateVault(options, failures);
        ValidateTransport(options, failures);
        ValidateRanges(options, failures);
        ValidateOllama(options, failures);
        ValidateHttp(options, failures);
        ValidateExternalRoots(options, failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateVault(KiokuOptions options, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(options.VaultPath))
        {
            failures.Add("KIOKU_VAULT_PATH (Kioku:VaultPath) is required.");
            return;
        }

        try
        {
            var path = Path.GetFullPath(options.VaultPath);
            if (!Directory.Exists(path))
            {
                failures.Add($"KIOKU_VAULT_PATH does not exist or is not a directory: '{path}'.");
                return;
            }

            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToArray();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            failures.Add($"KIOKU_VAULT_PATH is not accessible: {ex.Message}");
        }
    }

    private static void ValidateTransport(KiokuOptions options, ICollection<string> failures)
    {
        if (!options.Transport.Equals("stdio", StringComparison.OrdinalIgnoreCase) &&
            !options.Transport.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("KIOKU_TRANSPORT must be either 'stdio' or 'http'.");
        }
    }

    private static void ValidateRanges(KiokuOptions options, ICollection<string> failures)
    {
        AddRangeFailure(failures, options.MaxSearchResults, 1, 1000, "KIOKU_MAX_RESULTS");
        AddRangeFailure(failures, options.ObsidianBridgePort, 1, 65535, "KIOKU_OBSIDIAN_PORT");
        AddRangeFailure(failures, options.HttpPort, 1, 65535, "KIOKU_HTTP_PORT");
        AddRangeFailure(failures, options.HttpRequestTimeoutSeconds, 1, 3600, "KIOKU_HTTP_REQUEST_TIMEOUT_SECONDS");
        AddRangeFailure(failures, options.IndexConcurrency, 1, 128, "KIOKU_INDEX_CONCURRENCY");
        AddRangeFailure(failures, options.EmbeddingConcurrency, 1, 128, "KIOKU_EMBEDDING_CONCURRENCY");

        if (options.HttpMaxRequestBodyBytes is < 1024 or > 100 * 1024 * 1024)
        {
            failures.Add("KIOKU_HTTP_MAX_REQUEST_BODY_BYTES must be between 1024 and 104857600.");
        }
    }

    private static void ValidateOllama(KiokuOptions options, ICollection<string> failures)
    {
        if (!Uri.TryCreate(options.OllamaUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add("KIOKU_OLLAMA_URL must be an absolute HTTP or HTTPS URI.");
        }
    }

    private static void ValidateHttp(KiokuOptions options, ICollection<string> failures)
    {
        if (!IsValidHttpHost(options.HttpHost))
        {
            failures.Add("KIOKU_HTTP_HOST must be a host name, IP address, '*', or '+', without a URL scheme or path.");
        }

        foreach (var origin in options.HttpAllowedOrigins)
        {
            if (!HttpOrigin.TryNormalize(origin, out _))
            {
                failures.Add($"KIOKU_HTTP_ALLOWED_ORIGINS contains an invalid origin: '{origin}'.");
            }
        }

        foreach (var proxy in options.HttpTrustedProxies)
        {
            if (!IPAddress.TryParse(proxy, out _))
            {
                failures.Add($"KIOKU_HTTP_TRUSTED_PROXIES contains an invalid IP address: '{proxy}'.");
            }
        }

        if (options.IsHttpTransport && !IsLoopbackHost(options.HttpHost) &&
            string.IsNullOrWhiteSpace(options.ApiKey) && !options.AllowInsecureHttp)
        {
            failures.Add(
                "Non-loopback Streamable HTTP requires KIOKU_API_KEY. " +
                "Set KIOKU_ALLOW_INSECURE_HTTP=true only for an explicitly accepted unsafe deployment.");
        }
    }

    private static void ValidateExternalRoots(KiokuOptions options, ICollection<string> failures)
    {
        if (!options.AllowExternalReads)
        {
            return;
        }

        foreach (var root in options.ExternalReadRoots)
        {
            try
            {
                var fullPath = Path.GetFullPath(root);
                if (!Directory.Exists(fullPath))
                {
                    failures.Add($"KIOKU_EXTERNAL_READ_ROOTS contains a missing directory: '{fullPath}'.");
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failures.Add($"KIOKU_EXTERNAL_READ_ROOTS contains an invalid path '{root}': {ex.Message}");
            }
        }
    }

    private static void AddRangeFailure(
        ICollection<string> failures,
        int value,
        int minimum,
        int maximum,
        string variable)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{variable} must be between {minimum} and {maximum}.");
        }
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

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
}
