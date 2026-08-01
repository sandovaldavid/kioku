using Microsoft.AspNetCore.Http.Features;

namespace Kioku.Mcp.Server.Middleware;

/// <summary>Applies a bounded body size and execution timeout to Streamable HTTP POST calls.</summary>
public sealed class McpRequestLimitsMiddleware(RequestDelegate next, KiokuConfiguration config)
{
    private const string McpPath = "/mcp";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(McpPath, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = config.HttpMaxRequestBodyBytes;
        }

        if (context.Request.ContentLength > config.HttpMaxRequestBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync("[error] Request body exceeds the configured limit.");
            return;
        }

        // A Streamable HTTP GET can remain open for server-sent events. Bound only POST tool
        // calls so long-lived event streams are not disconnected by the execution timeout.
        if (!HttpMethods.IsPost(context.Request.Method))
        {
            await next(context);
            return;
        }

        var originalCancellation = context.RequestAborted;
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(config.HttpRequestTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            originalCancellation, timeout.Token);
        context.RequestAborted = linked.Token;

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested && !originalCancellation.IsCancellationRequested)
        {
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            context.Response.ContentType = "text/plain";
            await context.Response.WriteAsync(
                "[error] MCP request timed out.", CancellationToken.None);
        }
        finally
        {
            context.RequestAborted = originalCancellation;
        }
    }
}
