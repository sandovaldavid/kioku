using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Prompts;

/// <summary>
/// Curated MCP prompts exposing common Kioku workflows as native slash commands in any MCP
/// client. Prompts reference the implemented tools in docs/commands-reference.md.
/// </summary>
[McpServerPromptType]
public sealed class KiokuPrompts
{
    [McpServerPrompt(Name = "research_digest"), Description(
        "Summarizes recent reading/research activity in the vault and lists open questions.")]
    public static string research_digest(
        [Description("Folder to scope the review to (relative to vault root). Leave empty for the whole vault.")] string folder = "")
    {
        var scope = string.IsNullOrWhiteSpace(folder) ? "the whole vault" : $"the '{folder}' folder";
        return $"""
            Review recent research activity in {scope}:

            1. Call `get_work_context` with a suitable recent limit to see what changed recently.
            2. For each touched literature or research note, call `search_notes` with
               `mode='semantic'` and its key claim or topic to find related prior work.
            3. Summarize what was read, how it connects to existing notes, and open questions or
               gaps such as unresolved claims, missing citations, or contradictions.

            Keep the digest concise. Cite notes with [[wikilinks]] so the summary stays navigable.
            The research capability is disabled by default; if its tools are unavailable, report
            that the vault must enable the `research` capability before using citation audits.
            """;
    }

    [McpServerPrompt(Name = "process_inbox"), Description(
        "Guides the smart-inbox triage workflow: propose a plan, confirm it, then apply it.")]
    public static string process_inbox(
        [Description("Inbox folder to process (relative to vault root). Leave empty to use the configured default.")] string inbox = "")
    {
        var folderArg = string.IsNullOrWhiteSpace(inbox) ? "" : $" with inbox_folder='{inbox}'";
        return $"""
            Triage the inbox using the propose -> confirm -> apply flow:

            1. Call `process_inbox{folderArg}` with the default `apply=false` to get a numbered
               plan containing suggested folders, tags, and related links.
            2. Show the plan and ask which numbered items to accept.
            3. After explicit confirmation, call `process_inbox{folderArg}` again with `apply=true`
               and report each change.

            This moves and edits files. If the vault is a Git repository, native `git diff` and
            `git restore` can review or undo the changes. Never apply without explicit approval.
            """;
    }

    [McpServerPrompt(Name = "weekly_review"), Description(
        "Runs a weekly vault review: activity, overdue tasks, vault health, and link suggestions.")]
    public static string weekly_review() => """
        Run a weekly vault review:

        1. Call `get_work_context` with a recent limit for activity, inbox notes, and drafts.
        2. Call `list_tasks` with `overdue_only=true` for overdue work.
        3. Call `audit_vault` for empty, stale, untagged, broken, and unlinked notes.
        4. Call `suggest_links` without a `note` to propose bridges for orphan notes and graph
           islands.

        Combine the results into a readable summary grouped by Activity, Tasks, Vault health, and
        Link suggestions. Do not apply changes automatically.
        """;

    [McpServerPrompt(Name = "literature_review"), Description(
        "Collects existing evidence on a topic from the vault and synthesizes it with citations.")]
    public static string literature_review(
        [Description("Topic or research question to review.")] string topic) => $"""
        Perform a literature review on: "{topic}"

        1. Call `search_notes` with `mode='hybrid'` and the topic. If Ollama is unavailable, use
           `mode='keyword'`.
        2. Call `audit_citations` for the relevant folder to identify citation and metadata gaps.
        3. Synthesize what the vault says about "{topic}", where sources agree or disagree, and
           what remains to investigate. Cite every claim with a [[wikilink]].
        4. If asked to save the review, use `export_citations` for a references section, then
           write it with `create_note` or `edit_note`.
        """;

    [McpServerPrompt(Name = "resume_project"), Description(
        "Loads a project's engineering context before resuming work.")]
    public static string resume_project(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Resume work on project "{project}":

        1. Call `get_project_context` with project='{project}' to load the MOC, recent sessions,
           decisions, plans, bugs, tickets, backlog, and knowledge.
        2. `read_note` documents relevant to the task, starting with active plans and standing ADRs.
        3. Call `start_work_session` with project='{project}' and a goal.
        4. Summarize the current state and proposed next steps before writing code.

        When finished, call `end_work_session` with a useful handoff summary.
        """;

    [McpServerPrompt(Name = "record_decision"), Description(
        "Records an architecture decision for a project.")]
    public static string record_decision(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Topic of the decision, e.g. 'database choice'.")] string topic) => $"""
        Record an architecture decision about "{topic}" for project "{project}":

        1. Call `get_project_context` with project='{project}' and types='adr', then read related
           ADRs.
        2. Gather the context, decision, consequences, and rejected alternatives.
        3. Call `create_project_doc` with doc_type='adr', project='{project}', and those fields.
           Use status='proposed' until the user confirms it; use 'accepted' afterwards.
        4. If an older ADR is superseded, call `update_frontmatter` on it with status='superseded'
           and add a [[wikilink]] to the new ADR with `edit_note`.
        5. Call `suggest_links` on the new ADR and review the suggestions before applying them.
        """;

    [McpServerPrompt(Name = "log_bugfix"), Description(
        "Logs a bug and its fix for a project so future agents do not re-debug it.")]
    public static string log_bugfix(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Log the bug just fixed in project "{project}":

        1. Gather a title, symptom, root cause, fix, and related files from this session.
        2. Call `create_project_doc` with doc_type='bug', project='{project}', those fields, and
           status='fixed' (or 'open' if the fix is pending).
        3. Cross-reference related ADRs or plans with `edit_note` and [[wikilinks]].
        4. If a work session is active, append the bug to its log with `edit_note`.
        5. Call `suggest_links` on the bug note and review the suggestions before applying them.
        """;

    [McpServerPrompt(Name = "plan_feature"), Description(
        "Drafts an implementation plan for a feature after checking prior art.")]
    public static string plan_feature(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Feature to plan.")] string feature) => $"""
        Plan the feature "{feature}" for project "{project}":

        1. Call `get_project_context` with project='{project}' to load decisions, plans, and bugs.
        2. Call `search_notes` with `mode='hybrid'` and the feature topic to find prior art.
        3. Draft concrete checkbox steps and confirm the approach with the user.
        4. Call `create_project_doc` with doc_type='plan', project='{project}', status='draft' (or
           'active' if work starts immediately), and link a ticket when one exists.
        5. Use `edit_note` to check off completed steps and `update_frontmatter` to set status='done'.
        6. Call `suggest_links` on the new plan and review the suggestions before applying them.
        """;

    [McpServerPrompt(Name = "work_on_ticket"), Description(
        "Reads a human-written ticket, structures it, and creates a linked implementation plan.")]
    public static string work_on_ticket(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Ticket note name or path (under the project's tickets folder).")] string ticket) => $"""
        Work on ticket "{ticket}" of project "{project}":

        1. `read_note` the ticket to preserve the human's requirements.
        2. Call `get_project_context` with project='{project}' for decisions and prior work.
        3. Structure the ticket with `edit_note`, preserving its original requirements, and set
           status='in-progress' with `update_frontmatter`.
        4. Call `create_project_doc` with doc_type='plan', project='{project}', and ticket='{ticket}'.
        5. Link implementation notes back to the ticket and set its status to 'done' when complete.
        """;

    [McpServerPrompt(Name = "write_daily"), Description(
        "Drafts today's daily note for a project from recent sessions and the previous daily.")]
    public static string write_daily(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Draft today's daily note for project "{project}":

        1. Call `get_project_context` with project='{project}' and types='daily,session', then read
           the most recent daily note.
        2. Draft Yesterday from the previous daily and sessions, Today from open plans and tickets,
           and any open questions.
        3. Create the note with `create_note` in the project's daily folder, using today's date as
           the name, type='daily', status='active', and tags='daily'. Include the project link in
           the body.
        4. Show the draft so the user can edit it in Obsidian.
        """;
}
