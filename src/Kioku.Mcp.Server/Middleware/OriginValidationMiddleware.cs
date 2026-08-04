using Kioku.Mcp.Server.Http;
using Microsoft.Extensions.Logging;

namespace Kioku.Mcp.Server.Middleware;

/// <summary>
/// Enforces the MCP Streamable HTTP Origin requirement independently from CORS. Requests from
/// non-browser clients commonly omit Origin and remain valid; present origins must match exactly.
/// </summary>
public sealed class OriginValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OriginValidationMiddleware> _logger;
    private readonly HashSet<string> _allowedOrigins;

    public OriginValidationMiddleware(
        RequestDelegate next,
        KiokuConfiguration config,
        ILogger<OriginValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _allowedOrigins = config.HttpAllowedOrigins
            .Select(origin => HttpOrigin.TryNormalize(origin, out var normalized) ? normalized : string.Empty)
            .Where(origin => origin.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var values = context.Request.Headers.Origin;
        if (values.Count == 0)
        {
            await _next(context);
            return;
        }

        if (values.Count != 1 ||
            !HttpOrigin.TryNormalize(values[0], out var origin) ||
            !_allowedOrigins.Contains(origin))
        {
            _logger.Warn(
                "Rejected request from {RemoteIp}: invalid or disallowed Origin header.",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("[error] Forbidden — invalid Origin header.");
            return;
        }

        await _next(context);
    }
}
