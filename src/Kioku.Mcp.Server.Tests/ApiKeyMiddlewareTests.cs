using Kioku.Mcp.Server.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public class ApiKeyMiddlewareTests
{
    private static (ApiKeyMiddleware middleware, Func<bool> wasNextCalled) Create(string? apiKey)
    {
        var called = false;
        RequestDelegate next = _ =>
        {
            called = true;
            return Task.CompletedTask;
        };

        var config = new KiokuConfiguration { VaultPath = "/tmp", ApiKey = apiKey };
        var middleware = new ApiKeyMiddleware(next, config, NullLogger<ApiKeyMiddleware>.Instance);
        return (middleware, () => called);
    }

    private static DefaultHttpContext CreateContext(string path = "/mcp", string? bearerToken = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        if (bearerToken is not null)
        {
            context.Request.Headers.Authorization = $"Bearer {bearerToken}";
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task NoApiKeyConfigured_AllowsRequestThrough()
    {
        var (middleware, wasNextCalled) = Create(apiKey: null);
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.True(wasNextCalled());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HealthPath_IsAlwaysExempt_EvenWithApiKeyConfigured()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(path: "/health");

        await middleware.InvokeAsync(context);

        Assert.True(wasNextCalled());
    }

    [Fact]
    public async Task HealthSubPath_IsExempt_SegmentAware()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(path: "/health/sub");

        await middleware.InvokeAsync(context);

        Assert.True(wasNextCalled());
    }

    [Fact]
    public async Task PathThatMerelyStartsWithHealth_IsNotExempt()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(path: "/healthxyz");

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task CorrectBearerToken_AllowsRequestThrough()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(bearerToken: "secret");

        await middleware.InvokeAsync(context);

        Assert.True(wasNextCalled());
    }

    [Fact]
    public async Task MissingAuthorizationHeader_Returns401()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task WrongBearerToken_Returns401()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(bearerToken: "wrong");

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task TokenComparison_IsCaseSensitive()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "Secret");
        var context = CreateContext(bearerToken: "secret");

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task EmptyBearerToken_Returns401()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext(bearerToken: "");

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
    }

    [Fact]
    public async Task AuthorizationHeaderWithoutBearerScheme_Returns401()
    {
        var (middleware, wasNextCalled) = Create(apiKey: "secret");
        var context = CreateContext();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        await middleware.InvokeAsync(context);

        Assert.False(wasNextCalled());
    }

    [Fact]
    public async Task UnauthorizedResponse_HasExpectedBody()
    {
        var (middleware, _) = Create(apiKey: "secret");
        var context = CreateContext();

        await middleware.InvokeAsync(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body);
        var body = await reader.ReadToEndAsync();

        Assert.Equal("[error] Unauthorized — provide Authorization: Bearer <KIOKU_API_KEY>", body);
        Assert.Equal("text/plain", context.Response.ContentType);
    }
}
