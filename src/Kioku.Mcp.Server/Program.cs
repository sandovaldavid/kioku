using Kioku.Mcp.Server;
using Kioku.Mcp.Server.Logging;
using Kioku.Mcp.Server.Services;
using Kioku.Mcp.Server.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

// Host Builder
var builder = Host.CreateApplicationBuilder(args);

// Logs to stderr only — stdout is reserved for the MCP protocol (stdio)
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Information);

// Kioku Services
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<VaultIndexService>();
builder.Services.AddSingleton<ObsidianBridgeService>();

// MCP Server
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<NoteQueryTools>()
    .WithTools<NoteCommandTools>()
    .WithTools<ObsidianBridgeTools>()
    .WithTools<UtilityTools>();

// Index initialization on startup
var host = builder.Build();

var vaultIndex = host.Services.GetRequiredService<VaultIndexService>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.Info("Kioku MCP Server starting...");
logger.Info("Vault: {VaultPath}", config.VaultPath);

try
{
    // Index the vault before accepting MCP connections
    await vaultIndex.InitializeAsync();
}
catch (DirectoryNotFoundException)
{
    Console.Error.WriteLine($"[error] Vault not found: {config.VaultPath}");
    Console.Error.WriteLine("[error] Verify that KIOKU_VAULT_PATH points to a valid Obsidian vault.");
    return 2;
}

// Run MCP Server
await host.RunAsync();
return 0;
