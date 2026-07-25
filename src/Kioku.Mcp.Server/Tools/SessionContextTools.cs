using System.ComponentModel;
using Kioku.Mcp.Server.Services;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Tools;

/// <summary>
/// MCP adapter for durable, concurrency-safe work sessions and vault context awareness.
/// </summary>
[McpServerToolType]
public sealed partial class SessionContextTools
{
    private readonly IWorkSessionService _sessions;

    public SessionContextTools(IWorkSessionService sessions)
    {
        _sessions = sessions;
    }

    [McpServerTool, Description(
        "Returns a snapshot of the vault's current work state: inbox notes, drafts, recent activity, " +
        "and every active work session. Active sessions include their durable session_id.")]
    public Task<string> get_work_context(
        [Description("Folder treated as the inbox. Empty uses folders.inbox, then 'Inbox'.")] string inbox_folder = "",
        [Description("Maximum notes shown per section unless recent_limit is set.")] int max_per_section = 5,
        [Description("Optional vault-relative scope for recently modified notes.")] string recent_folder = "",
        [Description("Maximum recently modified notes. Zero uses max_per_section.")] int recent_limit = 0,
        CancellationToken cancellationToken = default) =>
        _sessions.GetWorkContextAsync(
            inbox_folder,
            max_per_section,
            recent_folder,
            recent_limit,
            cancellationToken);

    [McpServerTool, Description(
        "Starts a durable work session or resumes an active session by session_id. New sessions " +
        "receive a UUIDv7 identifier, collision-safe filename, UTC timestamps, project, agent, " +
        "MCP client identity, and optional parent_session_id. The response includes JSON.")]
    public Task<string> start_work_session(
        [Description("Optional human-readable session name; it is not used as identity.")] string session_name = "",
        [Description("Folder for global sessions. Empty uses folders.sessions, then 'Sessions'.")] string sessions_folder = "",
        [Description("Optional session goal.")] string goal = "",
        [Description("Project name; stores the note in the project's sessions folder.")] string project = "",
        [Description("Agent name. Empty auto-detects it from the MCP client.")] string agent = "",
        [Description("Existing durable session identifier to resume.")] string session_id = "",
        [Description("Optional parent session identifier for handoff chains.")] string parent_session_id = "",
        McpServer? server = null,
        CancellationToken cancellationToken = default) =>
        _sessions.StartAsync(
            session_name,
            sessions_folder,
            goal,
            project,
            agent,
            session_id,
            parent_session_id,
            server?.ClientInfo?.Name,
            cancellationToken);

    [McpServerTool, Description(
        "Closes an active work session. session_id is the primary selector. A note/path remains " +
        "supported for compatibility. Without an explicit selector, Kioku proceeds only when exactly " +
        "one active session matches the project and current MCP client/agent.")]
    public Task<string> end_work_session(
        [Description("Legacy explicit session note name or path. Prefer session_id.")] string session_note = "",
        [Description("Optional summary or outcome.")] string summary = "",
        [Description("Optional project scope for implicit resolution.")] string project = "",
        [Description("Durable session identifier; takes precedence over session_note.")] string session_id = "",
        [Description("Agent identity used when MCP client metadata is unavailable.")] string agent = "",
        McpServer? server = null,
        CancellationToken cancellationToken = default) =>
        _sessions.EndAsync(
            session_note,
            summary,
            project,
            session_id,
            agent,
            server?.ClientInfo?.Name,
            cancellationToken);

    [McpServerTool, Description(
        "Lists work sessions with durable IDs, agent/client identity, project, persisted UTC " +
        "timestamps, status, and duration. Activity uses started_at and ended_at.")]
    public Task<string> list_work_sessions(
        [Description("Folder containing global sessions. Empty auto-detects it.")] string sessions_folder = "",
        [Description("Project whose sessions should be listed.")] string project = "",
        [Description("Include notes modified during each session.")] bool include_activity = false,
        CancellationToken cancellationToken = default) =>
        _sessions.ListAsync(sessions_folder, project, include_activity, cancellationToken);

    internal static string NormalizeAgentName(string? raw) =>
        WorkSessionService.NormalizeAgentName(raw);

    internal static string WriteSummarySection(string content, string summary) =>
        WorkSessionService.WriteSummarySection(content, summary);
}
