using System.Text.Json;
using Kioku.Mcp.Server.Protocol;
using Kioku.Mcp.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace Kioku.Mcp.Server.Tests;

public sealed class KiokuToolAnnotationsTests
{
    internal static readonly Dictionary<string, (bool ReadOnly, bool Destructive, bool Idempotent, bool OpenWorld)> ReviewedToolMatrix = new(StringComparer.Ordinal)
    {
        ["acquire_coordination_claim"] = (false, false, true, false),
        ["add_backlog_item"] = (false, false, false, false),
        ["apply_template"] = (false, false, false, true),
        ["audit_citations"] = (true, false, true, false),
        ["audit_vault"] = (true, false, true, false),
        ["create_coordination_work_item"] = (false, false, false, false),
        ["create_folder_readme"] = (false, true, true, false),
        ["create_implementation_plan"] = (false, false, false, false),
        ["create_literature_note"] = (false, false, false, false),
        ["create_moc"] = (false, true, true, false),
        ["create_note"] = (false, false, false, false),
        ["create_project_doc"] = (false, false, false, false),
        ["create_regular_note"] = (false, false, false, false),
        ["create_zettel"] = (false, false, false, true),
        ["delete_note"] = (false, true, false, false),
        ["edit_in_obsidian"] = (false, true, false, true),
        ["edit_note"] = (false, true, false, false),
        ["end_work_session"] = (false, true, false, false),
        ["expire_coordination_claim"] = (false, false, true, false),
        ["export_citations"] = (true, false, true, false),
        ["find_duplicate_notes"] = (true, false, true, false),
        ["find_orphan_assets"] = (false, true, false, false),
        ["find_similar_notes"] = (true, false, true, true),
        ["generate_flashcards"] = (false, false, false, true),
        ["get_concept_map"] = (true, false, true, false),
        ["get_coordination_handoff"] = (true, false, true, false),
        ["get_coordination_work_item"] = (true, false, true, false),
        ["get_installed_plugins"] = (true, false, true, true),
        ["get_links"] = (true, false, true, false),
        ["get_obsidian_state"] = (true, false, true, true),
        ["get_project_context"] = (true, false, true, false),
        ["get_server_capabilities"] = (true, false, true, false),
        ["get_server_status"] = (true, false, true, false),
        ["get_vault_snapshot"] = (true, false, true, false),
        ["get_work_context"] = (true, false, true, false),
        ["import_bibtex"] = (false, false, false, true),
        ["lint"] = (false, true, false, true),
        ["list_coordination_blockers"] = (true, false, true, false),
        ["list_coordination_claims"] = (true, false, true, false),
        ["list_coordination_conflicts"] = (true, false, true, false),
        ["list_coordination_history"] = (true, false, true, false),
        ["list_coordination_runs"] = (true, false, true, false),
        ["list_coordination_work_items"] = (true, false, true, false),
        ["list_failed_coordination_attempts"] = (true, false, true, false),
        ["list_notes"] = (true, false, true, false),
        ["list_projects"] = (true, false, true, false),
        ["list_stale_coordination_work"] = (true, false, true, false),
        ["list_tasks"] = (true, false, true, false),
        ["list_work_sessions"] = (true, false, true, false),
        ["manage_css_snippets"] = (false, true, false, false),
        ["manage_tags"] = (false, true, false, false),
        ["manage_templates"] = (false, true, false, false),
        ["manage_trash"] = (false, true, false, false),
        ["move_note"] = (false, true, false, false),
        ["open_note_in_obsidian"] = (false, false, true, true),
        ["process_inbox"] = (false, true, false, false),
        ["query_dataview"] = (true, false, true, true),
        ["read_note"] = (true, false, true, false),
        ["rebuild_index"] = (false, false, true, false),
        ["record_adr"] = (false, false, false, false),
        ["record_bug"] = (false, false, false, false),
        ["release_coordination_claim"] = (false, false, true, false),
        ["renew_coordination_claim"] = (false, false, true, false),
        ["resolve_coordination_conflict"] = (false, false, true, false),
        ["save_project_knowledge"] = (false, false, false, false),
        ["search_notes"] = (true, false, true, true),
        ["set_task_state"] = (false, true, false, false),
        ["setup_agent_workflow"] = (false, false, true, false),
        ["start_work_session"] = (false, false, false, false),
        ["suggest_folder"] = (true, false, true, false),
        ["suggest_links"] = (false, true, false, false),
        ["suggest_tags"] = (true, false, true, false),
        ["summarize_note"] = (true, false, true, true),
        ["tidy_attachments"] = (false, true, false, false),
        ["transition_coordination_work_item"] = (false, false, false, false),
        ["trigger_obsidian_command"] = (false, true, false, true),
        ["update_frontmatter"] = (false, true, true, false),
    };

    [Theory]
    [InlineData("read_note")]
    [InlineData("get_project_context")]
    [InlineData("get_vault_snapshot")]
    [InlineData("audit_vault")]
    [InlineData("find_duplicate_notes")]
    [InlineData("get_server_status")]
    [InlineData("get_work_context")]
    [InlineData("suggest_folder")]
    [InlineData("suggest_tags")]
    public void Read_tools_are_closed_world_and_non_destructive(string toolName)
    {
        var annotations = KiokuToolAnnotations.Create(toolName);

        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Fact]
    public void Search_notes_is_read_only_but_dependency_aware()
    {
        var annotations = KiokuToolAnnotations.Create("search_notes");

        Assert.True(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.True(annotations.IdempotentHint);
        Assert.True(annotations.OpenWorldHint);
    }

    [Fact]
    public void Delete_note_is_destructive_and_not_retry_safe()
    {
        var annotations = KiokuToolAnnotations.Create("delete_note");

        Assert.False(annotations.ReadOnlyHint);
        Assert.True(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Fact]
    public void Create_note_is_additive_but_not_read_only()
    {
        var annotations = KiokuToolAnnotations.Create("create_note");

        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Theory]
    [InlineData("summarize_note")]
    [InlineData("query_dataview")]
    [InlineData("trigger_obsidian_command")]
    public void Dependency_or_bridge_tools_are_open_world(string toolName)
    {
        Assert.True(KiokuToolAnnotations.Create(toolName).OpenWorldHint);
    }

    [Fact]
    public void Every_tool_gets_an_explicit_complete_annotation_object()
    {
        var annotations = KiokuToolAnnotations.Create("future_tool");

        Assert.Equal("Future Tool", annotations.Title);
        Assert.False(annotations.ReadOnlyHint);
        Assert.False(annotations.DestructiveHint);
        Assert.False(annotations.IdempotentHint);
        Assert.False(annotations.OpenWorldHint);
    }

    [Fact]
    public void All_77_tools_match_reviewed_annotation_matrix()
    {
        Assert.Equal(77, ReviewedToolMatrix.Count);

        foreach (var (toolName, (expectedReadOnly, expectedDestructive, expectedIdempotent, expectedOpenWorld)) in ReviewedToolMatrix)
        {
            var annotations = KiokuToolAnnotations.Create(toolName);

            Assert.Equal(expectedReadOnly, annotations.ReadOnlyHint);
            Assert.Equal(expectedDestructive, annotations.DestructiveHint);
            Assert.Equal(expectedIdempotent, annotations.IdempotentHint);
            Assert.Equal(expectedOpenWorld, annotations.OpenWorldHint);
        }
    }

    [Fact]
    public void ReadOnly_tools_are_never_destructive_and_always_idempotent()
    {
        foreach (var (toolName, (readOnly, destructive, idempotent, _)) in ReviewedToolMatrix)
        {
            if (readOnly)
            {
                Assert.False(destructive, $"Tool '{toolName}' is marked read-only but classified as destructive.");
                Assert.True(idempotent, $"Tool '{toolName}' is marked read-only but not classified as idempotent.");
            }
        }
    }

    [Fact]
    public void Mutating_and_mixed_tools_are_never_read_only()
    {
        string[] mutatingOrMixed =
        [
            "create_note", "edit_note", "delete_note", "move_note", "update_frontmatter",
            "process_inbox", "suggest_links", "manage_trash", "manage_templates", "manage_tags",
            "manage_css_snippets", "tidy_attachments", "lint", "edit_in_obsidian",
            "trigger_obsidian_command", "set_task_state", "start_work_session", "end_work_session",
            "rebuild_index", "setup_agent_workflow", "import_bibtex", "generate_flashcards",
        ];

        foreach (var toolName in mutatingOrMixed)
        {
            var annotations = KiokuToolAnnotations.Create(toolName);
            Assert.False(annotations.ReadOnlyHint, $"Mutating or mixed tool '{toolName}' was incorrectly marked as read-only.");
        }
    }
}
