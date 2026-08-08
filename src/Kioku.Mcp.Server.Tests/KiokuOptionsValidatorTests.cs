using Kioku.Mcp.Server.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuOptionsValidatorTests : IDisposable
{
    private readonly string _vaultPath = Path.Combine(
        Path.GetTempPath(),
        $"kioku-options-{Guid.NewGuid():N}");

    public KiokuOptionsValidatorTests()
    {
        Directory.CreateDirectory(_vaultPath);
    }

    [Fact]
    public void Valid_stdio_configuration_succeeds()
    {
        var result = new KiokuOptionsValidator().Validate(
            Options.DefaultName,
            CreateValidOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_or_empty_vault_path_fails_validation()
    {
        var options = CreateValidOptions();
        options.VaultPath = "";

        var result = new KiokuOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("KIOKU_VAULT_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_existent_vault_path_fails_validation()
    {
        var options = CreateValidOptions();
        options.VaultPath = Path.Combine(Path.GetTempPath(), $"nonexistent-vault-{Guid.NewGuid():N}");

        var result = new KiokuOptionsValidator().Validate(Options.DefaultName, options);

        Assert.True(result.Failed);
        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("KIOKU_VAULT_PATH", StringComparison.Ordinal));
    }

    [Fact]
    public void Invalid_ranges_transport_and_uri_fail_with_actionable_messages()
    {
        var options = CreateValidOptions();
        options.Transport = "tcp";
        options.MaxSearchResults = 0;
        options.HttpPort = 70_000;
        options.OllamaUrl = "not-a-uri";
        options.IndexConcurrency = 0;
        options.EmbeddingConcurrency = 129;

        var result = new KiokuOptionsValidator().Validate(Options.DefaultName, options);
        var failures = result.Failures?.ToArray() ?? [];

        Assert.True(result.Failed);
        Assert.Contains(failures, failure => failure.Contains("KIOKU_TRANSPORT", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("KIOKU_MAX_RESULTS", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("KIOKU_HTTP_PORT", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("KIOKU_OLLAMA_URL", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("KIOKU_INDEX_CONCURRENCY", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("KIOKU_EMBEDDING_CONCURRENCY", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_loopback_http_requires_authentication_by_default()
    {
        var options = CreateValidOptions();
        options.Transport = "http";
        options.HttpHost = "0.0.0.0";

        var result = new KiokuOptionsValidator().Validate(Options.DefaultName, options);

        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("KIOKU_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void Legacy_environment_variables_are_projected_into_the_options_section()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KIOKU_VAULT_PATH"] = _vaultPath,
            ["KIOKU_MAX_RESULTS"] = "42",
            ["KIOKU_HTTP_ALLOWED_ORIGINS"] = "https://one.example,https://two.example",
            ["KIOKU_EXTERNAL_READ_ROOTS"] = string.Join(Path.PathSeparator, _vaultPath, Path.GetTempPath()),
            ["KIOKU_GITHUB_TOKEN"] = "unused",
        };

        var values = KiokuOptionsConfiguration.GetLegacyValues(
            name => environment.TryGetValue(name, out var value) ? value : null);

        Assert.Equal(_vaultPath, values["Kioku:VaultPath"]);
        Assert.Equal("42", values["Kioku:MaxSearchResults"]);
        Assert.Equal("https://one.example", values["Kioku:HttpAllowedOrigins:0"]);
        Assert.Equal("https://two.example", values["Kioku:HttpAllowedOrigins:1"]);
        Assert.Equal(_vaultPath, values["Kioku:ExternalReadRoots:0"]);
        Assert.DoesNotContain("Kioku:GitHubToken", values.Keys);
    }

    public void Dispose()
    {
        if (Directory.Exists(_vaultPath))
        {
            Directory.Delete(_vaultPath, recursive: true);
        }
    }

    private KiokuOptions CreateValidOptions() => new()
    {
        VaultPath = _vaultPath,
    };
}
