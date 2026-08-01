using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Hosting;
using Kioku.Mcp.Server.Http;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Prompts;
using Kioku.Mcp.Server.Protocol;
using Kioku.Mcp.Server.Resources;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Sentry;

var configuration = KiokuOptionsConfiguration.Build(args);
KiokuOptions options;
try
{
    options = KiokuOptionsConfiguration.GetValidated(configuration);
}
catch (OptionsValidationException ex)
{
    foreach (var failure in ex.Failures)
    {
        BootstrapLogger.Error($"Configuration: {failure}");
    }

    return 1;
}

var config = options.ToConfiguration();
try
{
    return options.IsHttpTransport
        ? await RunHttpAsync(configuration, config, args)
        : await RunStdioAsync(configuration, config, args);
}
catch (OptionsValidationException ex)
{
    foreach (var failure in ex.Failures)
    {
        BootstrapLogger.Error($"Configuration: {failure}");
    }

    return 1;
}
catch (DirectoryNotFoundException ex)
{
    BootstrapLogger.Error($"Vault initialization: {ex.Message}");
    return 2;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    BootstrapLogger.Error($"Startup failed: {ex.Message}");
    return 1;
}

static void ConfigureKiokuTools(IMcpServerBuilder builder, VaultCapabilityProfile capabilities)
{
    builder
        .WithTools<NoteQueryTools>()
        .WithTools<NoteCommandTools>()
        .WithTools<FocusedCreationTools>()
        .WithTools<UtilityTools>();

    if (capabilities.IsEnabled("tasks"))
    {
        builder.WithTools<TaskManagementTools>();
    }

    if (capabilities.IsEnabled("organization"))
    {
        builder.WithTools<VaultOrganizationTools>();
    }

    if (capabilities.IsEnabled("sessions"))
    {
        builder.WithTools<SessionContextTools>();
    }

    if (capabilities.IsEnabled("workflows"))
    {
        builder.WithTools<WorkflowTools>();
    }

    if (capabilities.IsEnabled("css"))
    {
        builder.WithTools<CssThemingTools>();
    }

    if (capabilities.IsEnabled("graph"))
    {
        builder.WithTools<KnowledgeGraphTools>();
        builder.WithTools<GraphAnalysisTools>();
    }

    if (capabilities.IsEnabled("research"))
    {
        builder.WithTools<SecureResearchTools>();
    }

    if (capabilities.IsEnabled("bridge"))
    {
        builder.WithTools<ObsidianBridgeTools>();
    }

    if (capabilities.IsEnabled("plugin"))
    {
        builder.WithTools<PluginIntegrationTools>();
    }

    if (capabilities.IsEnabled("assets"))
    {
        builder.WithTools<AssetTools>();
    }

    if (capabilities.IsEnabled("generation"))
    {
        builder.WithTools<GenerationTools>();
    }

    if (capabilities.IsEnabled("engineering"))
    {
        builder.WithTools<EngineeringWorkflowTools>();
    }

    if (capabilities.IsEnabled("coordination"))
    {
        builder.WithTools<CoordinationTools>();
    }

    builder.WithKiokuTypedResults();
}

static void ConfigureKiokuPromptsAndResources(
    IMcpServerBuilder builder,
    VaultCapabilityProfile capabilities)
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

    if (capabilities.IsEnabled("coordination"))
    {
        builder.WithResources<CoordinationResources>();
    }
}

static void ConfigureLogging(ILoggingBuilder logging)
{
    logging.ClearProviders();
    logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
    logging.SetMinimumLevel(LogLevel.Trace);
}

static void ConfigureSentry(KiokuConfiguration config)
{
    if (string.IsNullOrWhiteSpace(config.SentryDsn))
    {
        return;
    }

    SentrySdk.Init(options =>
    {
        ConfigureSentryOptions(options, config);
    });
}

static void ConfigureSentryOptions(SentryOptions options, KiokuConfiguration config)
{
    options.Dsn = config.SentryDsn;
    options.Release = typeof(Program).Assembly.GetName().Version?.ToString();
    options.TracesSampleRate = 0.0;
    options.ProfilesSampleRate = 0.0;
    options.AutoSessionTracking = false;
    options.SendDefaultPii = false;
    options.MaxBreadcrumbs = 50;
    options.SetBeforeSend((sentryEvent, _) =>
    {
        // The Sentry integration is opt-in, but an opt-in exporter still must not receive raw
        // exception payloads from the coordination boundary. SendDefaultPii remains disabled so
        // request, user, and breadcrumb data are not added by the SDK.
        sentryEvent.ServerName = null;
        if (sentryEvent.SentryExceptions is { } exceptions)
        {
            foreach (var exception in exceptions)
            {
                exception.Value = "redacted";
                exception.Stacktrace = null;
            }
        }

        return sentryEvent;
    });
}

static async Task<int> RunHttpAsync(
    IConfiguration configuration,
    KiokuConfiguration config,
    string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    builder.Configuration.AddConfiguration(configuration);
    builder.WebHost.UseUrls(config.HttpListenUrl);

    if (!string.IsNullOrWhiteSpace(config.SentryDsn))
    {
        builder.WebHost.UseSentry(options =>
        {
            ConfigureSentryOptions(options, config);
        });
    }

    ConfigureLogging(builder.Logging);
    builder.Services.AddKiokuRuntime(builder.Configuration);
    HttpTransportSecurity.ConfigureBuilder(builder, config);

    var capabilities = VaultCapabilityProfile.Load(config.VaultPath);
    var mcpBuilder = builder.Services.AddMcpServer().WithHttpTransport();
    ConfigureKiokuTools(mcpBuilder, capabilities);
    ConfigureKiokuPromptsAndResources(mcpBuilder, capabilities);

    var app = builder.Build();
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    logger.Info("Kioku MCP Server starting in Streamable HTTP mode...");
    logger.Info("Vault:     {VaultPath}", config.VaultPath);
    logger.Info("Endpoint:  {ListenUrl}/mcp", config.HttpListenUrl);
    logger.Info("Auth:      {AuthStatus}", config.HasApiKey ? "Bearer token enabled" : "disabled (loopback only)");
    if (!config.IsLoopbackHttpBinding && !config.HasApiKey)
    {
        logger.Warn(
            "UNSAFE OVERRIDE: unauthenticated Streamable HTTP is listening on non-loopback host {Host}.",
            config.HttpHost);
    }

    HttpTransportSecurity.Use(app, config);
    HttpTransportSecurity.MapHealthEndpoints(app);
    app.MapMcp("/mcp");

    await app.RunAsync();
    return 0;
}

static async Task<int> RunStdioAsync(
    IConfiguration configuration,
    KiokuConfiguration config,
    string[] args)
{
    ConfigureSentry(config);
    var builder = Host.CreateApplicationBuilder(args);
    builder.Configuration.AddConfiguration(configuration);
    ConfigureLogging(builder.Logging);
    builder.Services.AddKiokuRuntime(builder.Configuration);

    var capabilities = VaultCapabilityProfile.Load(config.VaultPath);
    var mcpBuilder = builder.Services.AddMcpServer().WithStdioServerTransport();
    ConfigureKiokuTools(mcpBuilder, capabilities);
    ConfigureKiokuPromptsAndResources(mcpBuilder, capabilities);

    using var host = builder.Build();
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.Info("Kioku MCP Server starting in stdio mode...");
    logger.Info("Vault: {VaultPath}", config.VaultPath);

    await host.RunAsync();
    return 0;
}
