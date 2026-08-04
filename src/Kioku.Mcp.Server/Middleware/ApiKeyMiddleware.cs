using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Middleware;

/// <summary>
/// ASP.NET Core middleware that validates Bearer token authentication for Streamable HTTP.
/// Only the minimal liveness endpoint is exempt when a key is configured.
/// </summary>
public sealed class ApiKeyMiddleware(RequestDelegate next, KiokuConfiguration config, ILogger<ApiKeyMiddleware> logger)
{
    internal const string LivenessPath = "/health/live";

    /// <summary>
    /// Validates the Authorization header against the configured API key.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // No API key configured: open access (local development only)
        if (!config.HasApiKey)
        {
            await next(context);
            return;
        }

        // Liveness is intentionally public and contains no configuration or dependency data.
        if (context.Request.Path.Value?.Equals(LivenessPath, StringComparison.OrdinalIgnoreCase) == true)
        {
            await next(context);
            return;
        }

        if (!TryExtractBearer(context.Request, out var token) ||
            !FixedTimeTokenEquals(config.ApiKey!, token))
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

    internal static bool FixedTimeTokenEquals(string expected, string actual)
    {
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(actual);
        var expectedHash = SHA256.HashData(expectedBytes);
        var actualHash = SHA256.HashData(actualBytes);

        try
        {
            // Hashing first gives FixedTimeEquals two equal-length inputs even when a caller
            // supplies a token with a different length from the configured secret.
            return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expectedBytes);
            CryptographicOperations.ZeroMemory(actualBytes);
            CryptographicOperations.ZeroMemory(expectedHash);
            CryptographicOperations.ZeroMemory(actualHash);
        }
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
