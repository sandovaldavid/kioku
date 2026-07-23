namespace Kioku.Mcp.Server.Services;

/// <summary>
/// Application boundary for durable work-session workflows.
/// MCP adapters depend on this contract instead of constructing workflow services directly.
/// </summary>
public interface IWorkSessionService
{
    Task<string> GetWorkContextAsync(
        string inboxFolder,
        int maxPerSection,
        string recentFolder,
        int recentLimit);

    Task<string> StartAsync(
        string sessionName,
        string sessionsFolder,
        string goal,
        string project,
        string agent,
        string sessionId,
        string parentSessionId,
        string? mcpClientName);

    Task<string> EndAsync(
        string sessionNote,
        string summary,
        string project,
        string sessionId,
        string agent,
        string? mcpClientName);

    Task<string> ListAsync(
        string sessionsFolder,
        string project,
        bool includeActivity);
}

internal sealed partial class WorkSessionService : IWorkSessionService
{
}
