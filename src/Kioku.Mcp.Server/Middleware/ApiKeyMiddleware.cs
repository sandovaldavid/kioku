using Kioku.Mcp.Server.Logging;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Middleware;

/// <summary>
/// ASP.NET Core middleware that validates Bearer token authentication for the HTTP-SSE transport.
/// When no API key is configured, all requests are allowed — suitable for trusted local networks.
/// The /health endpoint is always exempt from authentication.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, KiokuConfiguration config, ILogger<ApiKeyMiddleware> logger)
{
    private const string HealthPath = "/health";

    /// <summary>
    /// Validates the Authorization header against the configured API key.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // No API key configured: open access (local development only)
        if (string.IsNullOrEmpty(config.ApiKey))
        {
            await next(context);
            return;
        }

        // /health is always public — used by systemd, nginx, and monitoring tools
        if (context.Request.Path.StartsWithSegments(HealthPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        if (!TryExtractBearer(context.Request, out var token) || token != config.ApiKey)
        {
            logger.Warn(
                "Unauthorized request from {RemoteIp}: missing or invalid Bearer token.",
                context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                "[error] Unauthorized — provide Authorization: Bearer <KIOKU_API_KEY>");
            return;
        }

        await next(context);
    }

    private static bool TryExtractBearer(HttpRequest request, out string token)
    {
        token = string.Empty;

        if (!request.Headers.TryGetValue("Authorization", out var header))
        {
            return false;
        }

        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = value["Bearer ".Length..].Trim();
        return !string.IsNullOrEmpty(token);
    }
}
