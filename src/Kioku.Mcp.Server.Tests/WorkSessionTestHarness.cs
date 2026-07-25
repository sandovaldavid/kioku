global using SessionContextTools = Kioku.Mcp.Server.Tests.WorkSessionTestHarness;

using Kioku.Mcp.Server.Infrastructure;
using Kioku.Mcp.Server.Services;

namespace Kioku.Mcp.Server.Tests;

/// <summary>
/// Test-only facade that preserves the session integration-test vocabulary while exercising
/// <see cref="IWorkSessionService"/> directly. MCP adapter contracts are covered separately by
/// architecture and metadata tests.
/// </summary>
internal sealed class WorkSessionTestHarness
{
    private readonly IWorkSessionService _sessions;

    public WorkSessionTestHarness(IWorkSessionService sessions)
    {
        _sessions = sessions;
    }

    public WorkSessionTestHarness(
        VaultIndexService vault,
        KiokuConfiguration config,
        VaultConfigService vaultConfig,
        ProjectWorkspaceService workspace,
        ObsidianBridgeService bridge)
        : this(vault, config, vaultConfig, workspace, bridge, TimeProvider.System)
    {
    }

    public WorkSessionTestHarness(
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
            new WorkSessionFileSystem(),
            timeProvider))
    {
    }

    public Task<string> get_work_context(
        string inbox_folder = "",
        int max_per_section = 5,
        string recent_folder = "",
        int recent_limit = 0,
        CancellationToken cancellationToken = default) =>
        _sessions.GetWorkContextAsync(
            inbox_folder,
            max_per_section,
            recent_folder,
            recent_limit,
            cancellationToken);

    public Task<string> start_work_session(
        string session_name = "",
        string sessions_folder = "",
        string goal = "",
        string project = "",
        string agent = "",
        string session_id = "",
        string parent_session_id = "",
        CancellationToken cancellationToken = default) =>
        _sessions.StartAsync(
            session_name,
            sessions_folder,
            goal,
            project,
            agent,
            session_id,
            parent_session_id,
            mcpClientName: null,
            cancellationToken);

    public Task<string> end_work_session(
        string session_note = "",
        string summary = "",
        string project = "",
        string session_id = "",
        string agent = "",
        CancellationToken cancellationToken = default) =>
        _sessions.EndAsync(
            session_note,
            summary,
            project,
            session_id,
            agent,
            mcpClientName: null,
            cancellationToken);

    public Task<string> list_work_sessions(
        string sessions_folder = "",
        string project = "",
        bool include_activity = false,
        CancellationToken cancellationToken = default) =>
        _sessions.ListAsync(
            sessions_folder,
            project,
            include_activity,
            cancellationToken);

    internal static string NormalizeAgentName(string? raw) =>
        WorkSessionService.NormalizeAgentName(raw);

    internal static string WriteSummarySection(string content, string summary) =>
        WorkSessionService.WriteSummarySection(content, summary);
}
