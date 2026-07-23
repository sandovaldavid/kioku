using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Tools;

public sealed partial class SessionContextTools
{
    /// <summary>
    /// Internal compatibility constructor for existing integration fixtures.
    /// Production activation uses the public <see cref="IWorkSessionService"/> constructor.
    /// </summary>
    internal SessionContextTools(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge)
        : this(vault, config, vaultConfig, workspace, bridge, TimeProvider.System)
    {
    }

    /// <summary>
    /// Internal compatibility constructor for deterministic-clock integration fixtures.
    /// </summary>
    internal SessionContextTools(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge,
        TimeProvider timeProvider)
        : this(new WorkSessionService(
            vault,
            config,
            vaultConfig,
            workspace,
            bridge,
            timeProvider))
    {
    }
}
