using System.Net;
using Kioku.Mcp.Server.Middleware;
using Microsoft.AspNetCore.HttpOverrides;

namespace Kioku.Mcp.Server.Http;

/// <summary>Shared Streamable HTTP security, limits, proxy, CORS, and health wiring.</summary>
internal static class HttpTransportSecurity
{
    internal const string ReadinessPath = "/health/ready";

    public static void ConfigureBuilder(WebApplicationBuilder builder, KiokuConfiguration config)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = config.HttpMaxRequestBodyBytes;
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
        });

        builder.Services.AddCors(options =>
            options.AddDefaultPolicy(policy =>
                policy
                    .WithOrigins(config.HttpAllowedOrigins
                        .Select(origin =>
                            HttpOrigin.TryNormalize(origin, out var normalized) ? normalized : origin)
                        .ToArray())
                    .AllowAnyHeader()
                    .AllowAnyMethod()));

        if (config.HttpTrustedProxies.Count == 0)
        {
            return;
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (var proxy in config.HttpTrustedProxies)
            {
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            }
        });
    }

    public static void Use(WebApplication app, KiokuConfiguration config)
    {
        if (config.HttpTrustedProxies.Count > 0)
        {
            app.UseForwardedHeaders();
        }

        // Origin enforcement is deliberately separate from and before CORS. CORS controls
        // browser response access; it does not reject DNS-rebinding attempts by itself.
        app.UseMiddleware<OriginValidationMiddleware>();
        app.UseCors();
        app.UseMiddleware<ApiKeyMiddleware>();
        app.UseMiddleware<McpRequestLimitsMiddleware>();
    }

    public static void MapHealthEndpoints(WebApplication app)
    {
        app.MapGet(ApiKeyMiddleware.LivenessPath, () => Results.Ok(new { status = "ok" }));

        app.MapGet(ReadinessPath, (HttpReadinessState readiness) =>
        {
            var snapshot = readiness.GetSnapshot();
            return Results.Json(
                new
                {
                    status = snapshot.IsReady ? "ready" : "not_ready",
                    updated_at_utc = readiness.LastUpdatedUtc,
                    components = new
                    {
                        index = snapshot.Index,
                        embeddings = snapshot.Embeddings,
                        generation = snapshot.Generation,
                    },
                },
                statusCode: snapshot.IsReady
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable);
        });
    }
}
