namespace Kioku.Mcp.Server;

/// <summary>
/// Configuration for Kioku MCP Server.
/// Read from environment variables or command-line arguments.
/// </summary>
public sealed class KiokuConfiguration
{
    /// <summary>
    /// Absolute path to the root of the Obsidian vault.
    /// Environment variable: KIOKU_VAULT_PATH
    /// </summary>
    public required string VaultPath { get; init; }

    /// <summary>
    /// Maximum number of search results to return.
    /// Default: 20. Environment variable: KIOKU_MAX_RESULTS
    /// </summary>
    public int MaxSearchResults { get; init; } = 20;

    /// <summary>
    /// WebSocket port of the Obsidian plugin.
    /// Default: 7765. Environment variable: KIOKU_OBSIDIAN_PORT
    /// </summary>
    public int ObsidianBridgePort { get; init; } = 7765;

    /// <summary>
    /// Loads the configuration from environment variables.
    /// </summary>
    public static KiokuConfiguration FromEnvironment()
    {
        var vaultPath = Environment.GetEnvironmentVariable("KIOKU_VAULT_PATH")
            ?? throw new InvalidOperationException(
                "The KIOKU_VAULT_PATH environment variable is not configured. " +
                "Set KIOKU_VAULT_PATH with the path to your Obsidian vault.\n" +
                "Example: export KIOKU_VAULT_PATH=\"/home/user/my-vault\"");

        var maxResults = int.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_MAX_RESULTS"), out var r) ? r : 20;

        var port = int.TryParse(
            Environment.GetEnvironmentVariable("KIOKU_OBSIDIAN_PORT"), out var p) ? p : 7765;

        return new KiokuConfiguration
        {
            VaultPath = Path.GetFullPath(vaultPath),
            MaxSearchResults = maxResults,
            ObsidianBridgePort = port,
        };
    }
}
