using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class HttpConfigurationTests
{
    [Fact]
    public void Defaults_ToLoopbackAndAllowsUnauthenticatedLocalUse()
    {
        var config = Create();

        config.ValidateHttpTransport();

        Assert.True(config.IsLoopbackHttpBinding);
        Assert.Equal("http://127.0.0.1:5173", config.HttpListenUrl);
    }

    [Fact]
    public void NonLoopbackWithoutAuthentication_IsRejected()
    {
        var config = Create(httpHost: "0.0.0.0");

        var exception = Assert.Throws<InvalidOperationException>(config.ValidateHttpTransport);

        Assert.Contains("Refusing an unauthenticated non-loopback", exception.Message);
    }

    [Fact]
    public void NonLoopbackWithApiKey_IsAllowed()
    {
        var config = Create(httpHost: "0.0.0.0", apiKey: "secret");

        config.ValidateHttpTransport();
    }

    [Fact]
    public void NonLoopbackRequiresExplicitInsecureOverrideWhenNoApiKeyExists()
    {
        var config = new KiokuConfiguration
        {
            VaultPath = "/tmp",
            HttpHost = "*",
            AllowInsecureHttp = true,
        };

        config.ValidateHttpTransport();
    }

    [Theory]
    [InlineData("http://allowed.example/path")]
    [InlineData("https://user@allowed.example")]
    [InlineData("null")]
    [InlineData("not an origin")]
    public void InvalidAllowedOrigin_FailsStartup(string origin)
    {
        var config = new KiokuConfiguration
        {
            VaultPath = "/tmp",
            HttpAllowedOrigins = [origin],
        };

        Assert.Throws<InvalidOperationException>(config.ValidateHttpTransport);
    }

    [Fact]
    public void Ipv6Loopback_ProducesBracketedListenUrl()
    {
        var config = Create(httpHost: "::1");

        config.ValidateHttpTransport();

        Assert.True(config.IsLoopbackHttpBinding);
        Assert.Equal("http://[::1]:5173", config.HttpListenUrl);
    }

    private static KiokuConfiguration Create(
        string httpHost = "127.0.0.1",
        string? apiKey = null) =>
        new()
        {
            VaultPath = "/tmp",
            HttpHost = httpHost,
            ApiKey = apiKey,
        };
}
