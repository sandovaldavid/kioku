using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Middleware;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging;

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

// v2: HTTP-SSE Transport (Streamable HTTP)

static async Task<int> RunHttpAsync(KiokuConfiguration config, string[] args)
{
    var webBuilder = WebApplication.CreateBuilder(args);

    // Logs to stderr — stdout not used in HTTP mode but kept consistent
    webBuilder.Logging.ClearProviders();
    webBuilder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    webBuilder.Logging.SetMinimumLevel(LogLevel.Information);

    // Kioku services
    webBuilder.Services.AddSingleton(config);
    webBuilder.Services.AddSingleton<EmbeddingService>();
    webBuilder.Services.AddSingleton<VaultIndexService>();
    webBuilder.Services.AddSingleton<ObsidianBridgeService>();
    webBuilder.Services.AddSingleton<HybridSearchService>();
    webBuilder.Services.AddSingleton<TaskService>();

    // CORS: allow localhost and the Obsidian app origin
    webBuilder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy
                .WithOrigins("http://localhost", "app://obsidian.md")
                .AllowAnyHeader()
                .AllowAnyMethod()));

    // MCP over HTTP-SSE
    webBuilder.Services
        .AddMcpServer()
        .WithHttpTransport()
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
        .WithTools<UtilityTools>();

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
        Console.Error.WriteLine($"[error] Vault not found: {config.VaultPath}");
        Console.Error.WriteLine("[error] Verify that KIOKU_VAULT_PATH points to a valid Obsidian vault.");
        return 2;
    }

    await webApp.RunAsync($"http://localhost:{config.HttpPort}");
    return 0;
}

// v1: stdio Transport (default — backwards compatible)

static async Task<int> RunStdioAsync(KiokuConfiguration config)
{
    var builder = Host.CreateApplicationBuilder();

    // Logs to stderr only — stdout is reserved for the MCP protocol (stdio)
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Logging.SetMinimumLevel(LogLevel.Information);

    // Kioku services
    builder.Services.AddSingleton(config);
    builder.Services.AddSingleton<EmbeddingService>();
    builder.Services.AddSingleton<VaultIndexService>();
    builder.Services.AddSingleton<ObsidianBridgeService>();
    builder.Services.AddSingleton<HybridSearchService>();
    builder.Services.AddSingleton<TaskService>();

    // MCP over stdio
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
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
        .WithTools<UtilityTools>();

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
        Console.Error.WriteLine($"[error] Vault not found: {config.VaultPath}");
        Console.Error.WriteLine("[error] Verify that KIOKU_VAULT_PATH points to a valid Obsidian vault.");
        return 2;
    }

    await host.RunAsync();
    return 0;
}
