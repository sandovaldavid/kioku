using System.ComponentModel;
using Kioku.Mcp.Server.Domain;
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
        [Description("Optional coordination run identity. Requires work_item_id when supplied.")] string run_id = "",
        [Description("Optional coordination work-item identity. Requires run_id when supplied.")] string work_item_id = "",
        [Description("Optional coordination attempt identity. Empty generates one for a new link.")] string attempt_id = "",
        [Description("Expected SHA-256 revision from a prior read when resuming; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias when resuming; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the session note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the session note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "",
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
            coordination: WorkSessionCoordinationRequest.FromToolArguments(run_id, work_item_id, attempt_id),
            preconditions: VaultMutationPreconditions.FromToolArguments(
                expected_revision,
                expected_hash,
                claim_id,
                fence_generation,
                resource_key,
                mutation_id),
            cancellationToken: cancellationToken);

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
        [Description("Expected SHA-256 revision from a prior read; empty keeps legacy behavior.")] string expected_revision = "",
        [Description("Expected SHA-256 hash alias; empty keeps legacy behavior.")] string expected_hash = "",
        [Description("Current claim ID protecting the session note, when fencing is required.")] string claim_id = "",
        [Description("Current claim fence generation, when fencing is required.")] long fence_generation = 0,
        [Description("Canonical resource key; normally derived from the session note path.")] string resource_key = "",
        [Description("Optional idempotency key for retrying the same mutation.")] string mutation_id = "",
        McpServer? server = null,
        CancellationToken cancellationToken = default) =>
        _sessions.EndAsync(
            session_note,
            summary,
            project,
            session_id,
            agent,
            server?.ClientInfo?.Name,
            VaultMutationPreconditions.FromToolArguments(
                expected_revision,
                expected_hash,
                claim_id,
                fence_generation,
                resource_key,
                mutation_id),
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
