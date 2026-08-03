using Kioku.Mcp.Server.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class HttpConfigurationTests
{
    [Fact]
    public void Defaults_ToLoopbackAndAllowsUnauthenticatedLocalUse()
    {
        var options = Create();

        var result = Validate(options);
        var config = options.ToConfiguration();

        Assert.True(result.Succeeded);
        Assert.True(config.IsLoopbackHttpBinding);
        Assert.Equal("http://127.0.0.1:5173", config.HttpListenUrl);
    }

    [Fact]
    public void NonLoopbackWithoutAuthentication_IsRejected()
    {
        var result = Validate(Create(httpHost: "0.0.0.0"));

        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("KIOKU_API_KEY", StringComparison.Ordinal));
    }

    [Fact]
    public void NonLoopbackWithApiKey_IsAllowed()
    {
        var result = Validate(Create(httpHost: "0.0.0.0", apiKey: "secret"));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void NonLoopbackRequiresExplicitInsecureOverrideWhenNoApiKeyExists()
    {
        var result = Validate(Create(httpHost: "*", allowInsecureHttp: true));

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("http://allowed.example/path")]
    [InlineData("https://user@allowed.example")]
    [InlineData("null")]
    [InlineData("not an origin")]
    public void InvalidAllowedOrigin_FailsStartup(string origin)
    {
        var options = Create();
        options.HttpAllowedOrigins = [origin];

        var result = Validate(options);

        Assert.Contains(
            result.Failures ?? [],
            failure => failure.Contains("KIOKU_HTTP_ALLOWED_ORIGINS", StringComparison.Ordinal));
    }

    [Fact]
    public void Ipv6Loopback_ProducesBracketedListenUrl()
    {
        var options = Create(httpHost: "::1");
        var result = Validate(options);
        var config = options.ToConfiguration();

        Assert.True(result.Succeeded);
        Assert.True(config.IsLoopbackHttpBinding);
        Assert.Equal("http://[::1]:5173", config.HttpListenUrl);
    }

    private static ValidateOptionsResult Validate(KiokuOptions options) =>
        new KiokuOptionsValidator().Validate(Options.DefaultName, options);

    private static KiokuOptions Create(
        string httpHost = "127.0.0.1",
        string? apiKey = null,
        bool allowInsecureHttp = false) =>
        new()
        {
            VaultPath = "/tmp",
            Transport = "http",
            HttpHost = httpHost,
            ApiKey = apiKey,
            AllowInsecureHttp = allowInsecureHttp,
        };
}
