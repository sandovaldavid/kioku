using ModelContextProtocol.Protocol;

namespace Kioku.Mcp.Server.Protocol;

/// <summary>
/// Central source of truth for MCP tool safety hints. Annotations are deliberately
/// applied at tools/list time so capability-gated tools and both transports expose
/// the same reviewed contract.
/// </summary>
internal static class KiokuToolAnnotations
{
    private static readonly HashSet<string> ReadOnlyTools = new(StringComparer.Ordinal)
    {
        "read_note", "list_notes", "search_notes", "get_links", "find_similar_notes",
        "get_project_context", "list_projects", "get_concept_map", "get_vault_snapshot",
        "get_obsidian_state", "get_installed_plugins", "query_dataview",
        "summarize_note", "list_tasks", "get_task", "get_daily_note",
        "list_templates", "read_template", "list_assets", "find_orphan_assets",
        "get_metrics", "health_check", "get_server_capabilities",
        "get_coordination_work_item", "list_coordination_work_items", "list_coordination_runs",
        "list_coordination_claims", "list_coordination_history", "get_coordination_handoff",
        "list_coordination_blockers", "list_stale_coordination_work", "list_failed_coordination_attempts",
        "list_coordination_conflicts",
        "audit_vault", "find_duplicate_notes", "get_server_status", "get_work_context",
        "list_work_sessions", "suggest_folder", "suggest_tags", "audit_citations", "export_citations",
    };

    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "delete_note", "edit_note", "move_note", "update_frontmatter", "manage_trash",
        "tidy_attachments", "manage_css_snippets", "lint", "edit_in_obsidian",
        "trigger_obsidian_command", "suggest_links", "create_moc", "create_folder_readme",
    };

    private static readonly HashSet<string> IdempotentTools = new(StringComparer.Ordinal)
    {
        "read_note", "list_notes", "search_notes", "get_links", "find_similar_notes",
        "get_project_context", "list_projects", "get_concept_map", "get_vault_snapshot",
        "get_obsidian_state", "get_installed_plugins", "query_dataview", "summarize_note",
        "setup_agent_workflow", "update_frontmatter", "open_note_in_obsidian",
        "get_metrics", "health_check", "get_server_capabilities", "create_moc", "create_folder_readme",
        "get_coordination_work_item", "list_coordination_work_items", "list_coordination_runs",
        "list_coordination_claims", "list_coordination_history", "get_coordination_handoff",
        "list_coordination_blockers", "list_stale_coordination_work", "list_failed_coordination_attempts",
        "list_coordination_conflicts",
        "acquire_coordination_claim", "renew_coordination_claim", "release_coordination_claim",
        "expire_coordination_claim", "resolve_coordination_conflict",
        "audit_vault", "find_duplicate_notes", "get_server_status", "get_work_context",
        "list_tasks", "list_work_sessions", "suggest_folder", "suggest_tags",
        "audit_citations", "export_citations", "get_daily_note", "list_templates",
        "read_template", "list_assets", "find_orphan_assets", "rebuild_index",
    };

    private static readonly HashSet<string> OpenWorldTools = new(StringComparer.Ordinal)
    {
        // Local processes/plugins are outside the vault's closed-world data boundary.
        "find_similar_notes", "search_notes", "summarize_note", "generate_flashcards",
        "get_obsidian_state", "open_note_in_obsidian", "edit_in_obsidian",
        "trigger_obsidian_command", "get_installed_plugins", "query_dataview",
        "apply_template", "lint", "import_bibtex", "create_zettel",
    };

    internal static ToolAnnotations Create(string toolName) => new()
    {
        Title = Humanize(toolName),
        ReadOnlyHint = ReadOnlyTools.Contains(toolName),
        DestructiveHint = DestructiveTools.Contains(toolName),
        IdempotentHint = IdempotentTools.Contains(toolName),
        OpenWorldHint = OpenWorldTools.Contains(toolName),
    };

    private static string Humanize(string name) => string.Join(
        ' ',
        name.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
