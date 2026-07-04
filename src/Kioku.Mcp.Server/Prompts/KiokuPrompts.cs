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
}
