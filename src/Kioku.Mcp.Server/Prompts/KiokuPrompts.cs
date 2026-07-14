using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Kioku.Mcp.Server.Prompts;

/// <summary>
/// Curated MCP prompts exposing common Kioku workflows as native slash commands in any MCP
/// client (Claude Code, Cursor, VS Code, ...). Prompts are plain instructional text that
/// reference existing tools by name — keep these in sync with docs/commands-reference.md.
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

            1. Call `get_recent_activity` (scoped to '{folder}' if provided) to see what's been
               created or modified recently.
            2. For each recently-touched note that looks like a literature/research note, call
               `search_notes_semantic` with its key claim or topic to surface related prior work
               already in the vault.
            3. Summarize the findings in a short digest: what was read, how it connects to
               existing notes, and — most importantly — a list of open questions or gaps you
               noticed (unresolved claims, missing citations, contradictions between notes).

            Keep the digest concise. Cite notes with [[wikilinks]] so the summary stays
            navigable.
            """;
    }

    [McpServerPrompt(Name = "process_inbox"), Description(
        "Guides the smart-inbox triage workflow: propose a plan, confirm it, then apply it.")]
    public static string process_inbox(
        [Description("Inbox folder to process (relative to vault root). Leave empty to use the configured default.")] string inbox = "")
    {
        var folderArg = string.IsNullOrWhiteSpace(inbox) ? "" : $" with inbox_folder='{inbox}'";
        return $"""
            Triage the inbox using the propose → confirm → apply flow:

            1. Call `process_inbox{folderArg}` with the default `apply=false` to get a numbered
               plan (suggested destination folder, tags, and related links per note).
            2. Show the plan and ask the user which numbered items to accept — they may want to
               skip a suggestion or two.
            3. Once confirmed, call `process_inbox{folderArg}` again with `apply=true` to execute
               the plan for the accepted notes. Report what changed per note.

            Remind the user that this moves files — `revert_all_uncommitted` (or git) can undo it
            if something looks wrong. Never call apply=true without an explicit go-ahead.
            """;
    }

    [McpServerPrompt(Name = "weekly_review"), Description(
        "Runs a weekly vault review: digest, overdue tasks, orphaned notes, and link suggestions.")]
    public static string weekly_review() => """
        Run a weekly vault review:

        1. Call `generate_digest` with `period='week'` for a structured overview of the week's
           activity, tasks, and drafts awaiting review.
        2. Call `list_overdue_tasks` for anything that slipped past its due date.
        3. Call `find_unlinked_notes` to spot notes that never made it into the graph.
        4. Call `suggest_links` (vault-wide, no `note` argument) to propose bridges for orphans
           and small graph islands.

        Combine the results into a single readable summary the user can act on, grouped by
        section (Activity, Tasks, Orphans, Link suggestions). Don't apply anything automatically
        — this is a review, not a cleanup pass.
        """;

    [McpServerPrompt(Name = "literature_review"), Description(
        "Collects existing evidence on a topic from the vault and synthesizes it with citations.")]
    public static string literature_review(
        [Description("Topic or research question to review.")] string topic) => $"""
        Perform a literature review on: "{topic}"

        1. Call `search_notes_hybrid` (or `search_notes_semantic` if Ollama is unavailable via
           `search_notes`) with the topic to find every note in the vault that already touches it.
        2. Call `get_literature_gap` for the topic to see what's thin or missing in the existing
           coverage.
        3. Synthesize the findings into a short literature review: what the vault already says
           about "{topic}", where sources agree or disagree, and what's left to investigate.
           Cite every claim back to its source note with a [[wikilink]] — never state a claim
           without pointing to where it came from.
        4. If asked to save the review, use `export_citations` for a references section before
           writing it out.
        """;

    [McpServerPrompt(Name = "resume_project"), Description(
        "Loads a project's full engineering context (decisions, plans, bugs, session handoffs) before resuming work.")]
    public static string resume_project(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Resume work on project "{project}":

        1. Call `get_project_context` with project='{project}' to load the project overview,
           recent session summaries (what previous agents did), decisions, plans, bugs, tickets,
           and backlog. Humans may have edited these in Obsidian since the last session — this
           tool always reads the current file contents.
        2. `read_note` any document that looks relevant to the task at hand (open plans first,
           then standing ADRs that constrain how you may implement things).
        3. Call `start_work_session` with project='{project}' and a goal, so this session is
           recorded for the next agent.
        4. Summarize for the user: current state, open plans, standing decisions, and what you
           propose to do next. Do not write code before this summary.

        When you finish, close with `end_work_session` and a summary — it becomes the handoff
        the next agent reads first.
        """;

    [McpServerPrompt(Name = "record_decision"), Description(
        "Records an architecture decision (ADR) for a project, superseding older ADRs when needed.")]
    public static string record_decision(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Topic of the decision, e.g. 'database choice'.")] string topic) => $"""
        Record an architecture decision about "{topic}" for project "{project}":

        1. Call `get_project_context` with project='{project}' and types='adr' to review prior
           decisions on this topic. `read_note` any ADR that looks related.
        2. Gather from the conversation (or ask the user): the context (forces/problem), the
           decision itself, its consequences, and the alternatives that were rejected.
        3. Call `record_adr` with those fields. Use status='proposed' if the user has not
           confirmed the decision yet; 'accepted' otherwise.
        4. If this decision supersedes an earlier ADR, call `update_frontmatter` on the old
           ADR setting status='superseded', and mention the new ADR in it with a [[wikilink]].
        """;

    [McpServerPrompt(Name = "log_bugfix"), Description(
        "Logs a bug and its fix for a project so future agents don't re-debug solved problems.")]
    public static string log_bugfix(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Log the bug that was just fixed in project "{project}":

        1. From this session, gather: a short title, the observed symptom, the actual root
           cause, the fix that was applied, and the files that were touched.
        2. Call `log_bug` with project='{project}' and those fields (status='fixed'; use
           status='open' if the fix is still pending).
        3. If the bug relates to an existing ADR or plan, `append_to_note` a [[wikilink]]
           cross-reference so the connection is navigable in Obsidian.
        4. If there is an active work session, record the bug in its '## Log' section too.
        """;

    [McpServerPrompt(Name = "plan_feature"), Description(
        "Drafts an implementation plan for a feature, checking prior art in the vault first.")]
    public static string plan_feature(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Feature to plan.")] string feature) => $"""
        Plan the feature "{feature}" for project "{project}":

        1. Call `get_project_context` with project='{project}' to load standing decisions
           (ADRs constrain the design), open plans, and known bugs.
        2. Call `search_notes_hybrid` with the feature topic to find prior art elsewhere in
           the vault.
        3. Draft the plan: objective and concrete steps as a markdown checkbox list
           ('- [ ] step'). Confirm the approach with the user before saving.
        4. Call `create_plan` with project='{project}', status='draft' (or 'active' if work
           starts immediately). Link the originating ticket via the ticket parameter if one
           exists.
        5. As steps complete, check them off with `update_note_content`; when the plan is
           done, set status='done' with `update_frontmatter`.
        """;

    [McpServerPrompt(Name = "work_on_ticket"), Description(
        "Reads a human-written ticket, structures it, and creates a linked implementation plan.")]
    public static string work_on_ticket(
        [Description("Project name (folder under the projects root).")] string project,
        [Description("Ticket note name or path (under the project's tickets folder).")] string ticket) => $"""
        Work on ticket "{ticket}" of project "{project}":

        1. `read_note` the ticket — the human wrote the idea, requirements, or objective there.
        2. Call `get_project_context` with project='{project}' to load the decisions and prior
           work that constrain the solution.
        3. Structure the ticket: rewrite its 'Objective' and 'Acceptance criteria' sections
           with `update_note_content`, keeping the human's original requirements intact.
           Set its status to 'in-progress' with `update_frontmatter`.
        4. Call `create_plan` with ticket='{ticket}' so the plan and ticket are cross-linked.
        5. While implementing, link any notes you create (bugs, knowledge, ADRs) back to the
           ticket with [[wikilinks]]. When done, set the ticket status to 'done'.
        """;

    [McpServerPrompt(Name = "write_daily"), Description(
        "Drafts today's daily note for a project from recent sessions and the previous daily.")]
    public static string write_daily(
        [Description("Project name (folder under the projects root).")] string project) => $"""
        Draft today's daily note for project "{project}":

        1. Call `get_project_context` with project='{project}' and types='daily,session' to
           find the previous daily note and recent session summaries. `read_note` the most
           recent daily.
        2. Draft today's note: 'Yesterday' from the previous daily and session summaries,
           'Today' from open plans and in-progress tickets, plus any open questions for the
           team you noticed.
        3. Create it with `create_note_from_template` using template 'kioku/daily' (or
           `create_note`) inside the project's daily/ subfolder, named after today's date
           (yyyy-MM-dd), with type 'daily'. Pass variables including project='{project}'
           so the template placeholders resolve.
        4. Show the draft to the user — the daily is primarily their note; they may edit it
           in Obsidian afterwards.
        """;
}
