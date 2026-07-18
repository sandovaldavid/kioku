using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Middleware;
using Kioku.Mcp.Server.Prompts;
using Kioku.Mcp.Server.Resources;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sentry;

// Configuration from environment variables
// Note: Uses BootstrapLogger because this occurs before DI/logging is configured.
KiokuConfiguration config;
try
{
    config = KiokuConfiguration.FromEnvironment();
}
catch (InvalidOperationException ex)
{
    BootstrapLogger.Error($"Configuration: {ex.Message}");
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
    services.AddSingleton<VaultPathPolicy>();
    services.AddSingleton<EmbeddingService>();
    services.AddSingleton<GenerationService>();
    services.AddSingleton<VaultIndexService>();
    services.AddSingleton<ObsidianBridgeService>();
    services.AddSingleton<HybridSearchService>();
    services.AddSingleton<TaskService>();
    services.AddSingleton<MetricsService>();
    services.AddSingleton<ProjectWorkspaceService>();
    // NoteCommandTools delegates structured create_note kinds to the shared implementation.
    services.AddTransient<ZettelkastenTools>();

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

static void ConfigureKiokuTools(IMcpServerBuilder builder, VaultConfigService vaultConfig)
{
    // Core tools are always available
    builder
        .WithTools<NoteQueryTools>()
        .WithTools<NoteCommandTools>()
        .WithTools<UtilityTools>();

    if (vaultConfig.IsGroupEnabled("tasks"))
    {
        builder.WithTools<TaskManagementTools>();
    }

    if (vaultConfig.IsGroupEnabled("organization"))
    {
        builder.WithTools<VaultOrganizationTools>();
    }

    if (vaultConfig.IsGroupEnabled("sessions"))
    {
        builder.WithTools<SessionContextTools>();
    }

    if (vaultConfig.IsGroupEnabled("workflows"))
    {
        builder.WithTools<WorkflowTools>();
    }

    if (vaultConfig.IsGroupEnabled("css"))
    {
        builder.WithTools<CssThemingTools>();
    }

    if (vaultConfig.IsGroupEnabled("graph"))
    {
        builder.WithTools<KnowledgeGraphTools>();
        builder.WithTools<GraphAnalysisTools>();
    }

    if (vaultConfig.IsGroupEnabled("research"))
    {
        builder.WithTools<SecureResearchTools>();
    }

    if (vaultConfig.IsGroupEnabled("bridge"))
    {
        builder.WithTools<ObsidianBridgeTools>();
    }

    if (vaultConfig.IsGroupEnabled("plugin"))
    {
        builder.WithTools<PluginIntegrationTools>();
    }

    if (vaultConfig.IsGroupEnabled("assets"))
    {
        builder.WithTools<AssetTools>();
    }

    if (vaultConfig.IsGroupEnabled("generation"))
    {
        builder.WithTools<GenerationTools>();
    }

    if (vaultConfig.IsGroupEnabled("engineering"))
    {
        builder.WithTools<EngineeringWorkflowTools>();
    }
}

static void ConfigureKiokuPromptsAndResources(IMcpServerBuilder builder)
{
    builder
        .WithPrompts<KiokuPrompts>()
        .WithResources<NoteResources>()
        .WithListResourcesHandler(async (ctx, _) =>
        {
            var vault = ctx.Services!.GetRequiredService<VaultIndexService>();

            var recent = vault.GetAllNotes()
                .OrderByDescending(n => n.LastModified)
                .Take(20)
                .Select(n => new Resource
                {
                    Uri = $"kioku://note/{n.VaultRelativePath.Replace('\\', '/')}",
                    Name = n.Name,
                    MimeType = "text/markdown",
                })
                .ToList();

            return await Task.FromResult(new ListResourcesResult { Resources = recent });
        });
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    logging.SetMinimumLevel(LogLevel.Information);
}

static void ConfigureSentry(KiokuConfiguration config)
{
    if (string.IsNullOrWhiteSpace(config.SentryDsn))
    {
        return;
    }

    SentrySdk.Init(options =>
    {
        options.Dsn = config.SentryDsn;
        options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
        options.TracesSampleRate = 0.0;
        options.ProfilesSampleRate = 0.0;
        options.AutoSessionTracking = false;
        options.SendDefaultPii = false;
        options.MaxBreadcrumbs = 50;
    });
}

// v2: HTTP-SSE Transport (Streamable HTTP)

static async Task<int> RunHttpAsync(KiokuConfiguration config, string[] args)
{
    var webBuilder = WebApplication.CreateBuilder(args);

    if (!string.IsNullOrWhiteSpace(config.SentryDsn))
    {
        webBuilder.WebHost.UseSentry(options =>
        {
            options.Dsn = config.SentryDsn;
            options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
            options.TracesSampleRate = 0.0;
            options.ProfilesSampleRate = 0.0;
            options.AutoSessionTracking = false;
            options.SendDefaultPii = false;
            options.MaxBreadcrumbs = 50;
        });
    }

    ConfigureLogging(webBuilder.Logging);
    ConfigureKiokuServices(webBuilder.Services, config);

    // Build VaultConfigService early so tool groups can be filtered at registration time.
    using var loggerFactory = LoggerFactory.Create(ConfigureLogging);
    var vaultConfig = new VaultConfigService(config, loggerFactory.CreateLogger<VaultConfigService>());
    webBuilder.Services.AddSingleton(vaultConfig);

    // CORS: allow localhost and the Obsidian app origin
    webBuilder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy
                .WithOrigins("http://localhost", "app://obsidian.md")
                .AllowAnyHeader()
                .AllowAnyMethod()));

    // MCP over HTTP-SSE
    var httpMcpBuilder = webBuilder.Services
        .AddMcpServer()
        .WithHttpTransport();
    ConfigureKiokuTools(httpMcpBuilder, vaultConfig);
    ConfigureKiokuPromptsAndResources(httpMcpBuilder);

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
    var embedding = webApp.Services.GetRequiredService<EmbeddingService>();
    var generation = webApp.Services.GetRequiredService<GenerationService>();
    var lifetime = webApp.Services.GetRequiredService<IHostApplicationLifetime>();
    try
    {
        await vaultIndex.InitializeAsync();
    }
    catch (DirectoryNotFoundException)
    {
        return 2;
    }

    await generation.InitializeAsync();

    lifetime.ApplicationStopping.Register(() =>
    {
        logger.Info("Shutting down: flushing embedding cache...");
        embedding.SaveAsync().GetAwaiter().GetResult();
        logger.Info("Embedding cache flushed.");
    });

    await webApp.RunAsync($"http://localhost:{config.HttpPort}");
    return 0;
}

// v1: stdio Transport (default — backwards compatible)

static async Task<int> RunStdioAsync(KiokuConfiguration config)
{
    ConfigureSentry(config);

    var builder = Host.CreateApplicationBuilder();
    ConfigureLogging(builder.Logging);
    ConfigureKiokuServices(builder.Services, config);

    // Build VaultConfigService early so tool groups can be filtered at registration time.
    using var loggerFactory = LoggerFactory.Create(ConfigureLogging);
    var vaultConfig = new VaultConfigService(config, loggerFactory.CreateLogger<VaultConfigService>());
    builder.Services.AddSingleton(vaultConfig);

    // MCP over stdio
    var stdioMcpBuilder = builder.Services
        .AddMcpServer()
        .WithStdioServerTransport();
    ConfigureKiokuTools(stdioMcpBuilder, vaultConfig);
    ConfigureKiokuPromptsAndResources(stdioMcpBuilder);

    var host = builder.Build();

    var vaultIndex = host.Services.GetRequiredService<VaultIndexService>();
    var embedding = host.Services.GetRequiredService<EmbeddingService>();
    var generation = host.Services.GetRequiredService<GenerationService>();
    var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
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

    await generation.InitializeAsync();

    lifetime.ApplicationStopping.Register(() =>
    {
        logger.Info("Shutting down: flushing embedding cache...");
        embedding.SaveAsync().GetAwaiter().GetResult();
        logger.Info("Embedding cache flushed.");
    });

    await host.RunAsync();
    return 0;
}
