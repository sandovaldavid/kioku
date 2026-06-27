using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Middleware;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

// Configuration from environment variables
KiokuConfiguration config;
try
{
    config = KiokuConfiguration.FromEnvironment();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"[error] Configuration: {ex.Message}");
    return 1;
}

// Check if --http flag was passed as CLI argument
var useHttp = config.IsHttpTransport || args.Contains("--http");

if (useHttp)
{
    return await RunHttpAsync(config, args);
}

return await RunStdioAsync(config);

static void ConfigureKiokuServices(IServiceCollection services, KiokuConfiguration config)
{
    services.AddSingleton(config);
    services.AddSingleton<VaultConfigService>();
    services.AddSingleton<EmbeddingService>();
    services.AddSingleton<VaultIndexService>();
    services.AddSingleton<ObsidianBridgeService>();
    services.AddSingleton<HybridSearchService>();
    services.AddSingleton<TaskService>();

    // Named HttpClient for Ollama
    services.AddHttpClient("ollama", c =>
    {
        c.BaseAddress = new Uri(config.OllamaUrl);
        c.Timeout = TimeSpan.FromSeconds(30);
    }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 4,
    });

    // Named HttpClient for web requests (ResearchTools)
    services.AddHttpClient("web", c =>
    {
        c.Timeout = TimeSpan.FromSeconds(30);
    }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
    });
}

static void ConfigureKiokuTools(IMcpServerBuilder builder)
{
    builder
        .WithTools<NoteQueryTools>()
        .WithTools<NoteCommandTools>()
        .WithTools<ObsidianBridgeTools>()
        .WithTools<TaskManagementTools>()
        .WithTools<ZettelkastenTools>()
        .WithTools<VaultOrganizationTools>()
        .WithTools<SessionContextTools>()
        .WithTools<WorkflowTools>()
        .WithTools<CssThemingTools>()
        .WithTools<KnowledgeGraphTools>()
        .WithTools<ResearchTools>()
        .WithTools<PluginIntegrationTools>()
        .WithTools<GraphAnalysisTools>()
        .WithTools<GitTools>()
        .WithTools<RestoreTools>()
        .WithTools<AssetTools>()
        .WithTools<UtilityTools>();
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    logging.SetMinimumLevel(LogLevel.Information);
}

// v2: HTTP-SSE Transport (Streamable HTTP)

static async Task<int> RunHttpAsync(KiokuConfiguration config, string[] args)
{
    var webBuilder = WebApplication.CreateBuilder(args);
    ConfigureLogging(webBuilder.Logging);
    ConfigureKiokuServices(webBuilder.Services, config);

    // CORS: allow localhost and the Obsidian app origin
    webBuilder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy
                .WithOrigins("http://localhost", "app://obsidian.md")
                .AllowAnyHeader()
                .AllowAnyMethod()));

    // MCP over HTTP-SSE
    ConfigureKiokuTools(webBuilder.Services
        .AddMcpServer()
        .WithHttpTransport());

    var webApp = webBuilder.Build();

    var logger = webApp.Services.GetRequiredService<ILogger<Program>>();
    logger.Info("Kioku MCP Server starting in HTTP-SSE mode...");
    logger.Info("Vault:     {VaultPath}", config.VaultPath);
    logger.Info("Endpoint:  http://localhost:{HttpPort}/mcp", config.HttpPort);
    logger.Info("Auth:      {AuthStatus}", string.IsNullOrEmpty(config.ApiKey) ? "disabled (no KIOKU_API_KEY set)" : "Bearer token enabled");

    // Middleware pipeline
    webApp.UseCors();
    webApp.UseMiddleware<ApiKeyMiddleware>();

    // Routes
    webApp.MapGet("/health", () => Results.Ok(new
    {
        status = "ok",
        transport = "http",
        vault = config.VaultPath,
        version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
    }));

    webApp.MapMcp("/mcp");

    // Initialize vault index before accepting connections
    var vaultIndex = webApp.Services.GetRequiredService<VaultIndexService>();
    try
    {
        await vaultIndex.InitializeAsync();
    }
    catch (DirectoryNotFoundException)
    {
        return 2;
    }

    await webApp.RunAsync($"http://localhost:{config.HttpPort}");
    return 0;
}

// v1: stdio Transport (default — backwards compatible)

static async Task<int> RunStdioAsync(KiokuConfiguration config)
{
    var builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder.Logging);
    ConfigureKiokuServices(builder.Services, config);

    // MCP over stdio
    ConfigureKiokuTools(builder.Services
        .AddMcpServer()
        .WithStdioServerTransport());

    var host = builder.Build();

    var vaultIndex = host.Services.GetRequiredService<VaultIndexService>();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();

    logger.Info("Kioku MCP Server starting in stdio mode...");
    logger.Info("Vault: {VaultPath}", config.VaultPath);

    try
    {
        await vaultIndex.InitializeAsync();
    }
    catch (DirectoryNotFoundException)
    {
        return 2;
    }

    await host.RunAsync();
    return 0;
}
