---
name: kioku-vault
description: Use when working with an Obsidian vault through the Kioku MCP server. Covers the current 45-tool default profile, 78-tool all-capabilities profile, first-class engineering specs, focused workflows, capability gating, guarded writes, sessions, and durable coordination.
---

# Kioku vault skill

Kioku reads, searches, and writes an Obsidian vault directly on disk. It works with Obsidian closed. The optional bridge matters only for Obsidian UI actions and plugin integrations.

Treat live MCP discovery as the runtime contract. For exact parameters, inspect the tool description or `docs/commands-reference.md`; do not infer schemas from examples or from the installed SDK version.

## Discover the active contract first

- Use MCP `tools/list`, prompts/resources discovery, or `get_server_capabilities` instead of assuming every optional group is exposed.
- The generated `develop` contract contains 45 default tools and 78 tools with every capability group enabled.
- Enabled by default: `tasks`, `organization`, `sessions`, `workflows`, `graph`, and `engineering`.
- Disabled by default: `research`, `generation`, `css`, `assets`, `bridge`, `plugin`, and `coordination`.
- An absent `capabilities` block preserves those defaults; it does not enable every optional group.
- With `capabilities.require_explicit: true`, only explicitly enabled optional groups are registered.
- A capability mismatch is normally configuration or connection scope, not evidence that note content is corrupt.

## Tool groups

- **Query**: `find_similar_notes`, `get_links`, `list_notes`, `read_note`, `search_notes`.
- **Focused note creation**: `create_regular_note`, `create_zettel`, `create_literature_note`, `create_moc`, `create_folder_readme`.
- **Mutation**: `delete_note`, `edit_note`, `manage_trash`, `move_note`, `update_frontmatter`.
- **Tasks**: `list_tasks`, `set_task_state`.
- **Organization**: `audit_vault`, `find_duplicate_notes`, `manage_tags`, `process_inbox`, `suggest_folder`, `suggest_tags`.
- **Sessions**: `end_work_session`, `get_work_context`, `list_work_sessions`, `start_work_session`.
- **Engineering**: `add_backlog_item`, `create_engineering_spec`, `create_implementation_plan`, `get_project_context`, `list_projects`, `record_adr`, `record_bug`, `save_project_knowledge`, `setup_agent_workflow`.
- **Templates and graph**: `manage_templates`, `get_concept_map`, `get_vault_snapshot`, `suggest_links`.
- **Coordination**: work items, transitions, claims, leases, history, handoffs, blockers, stale work, failed attempts, and conflicts.
- **Optional integrations**: research, local generation, CSS, assets, plugin bridge, and Obsidian UI tools.
- **Utilities**: `get_server_capabilities`, `get_server_status`, `rebuild_index`.

`create_note` and `create_project_doc` remain compatibility wrappers. Prefer the focused creation and engineering tools for new workflows because their contracts match one intent. First-class specs use `create_engineering_spec` directly; `create_project_doc` does not provide a spec compatibility mode.

## Search and reading

- Use `search_notes` with `mode='keyword'` for exact terms, tags, identifiers, or filenames.
- Use `mode='semantic'` only when embeddings are available and differently phrased concepts matter.
- Use `mode='hybrid'` as the normal discovery mode; it can fall back to keyword search.
- Use `list_notes` for folder and frontmatter filters.
- Use `read_note(metadata_only=true)` when the body is unnecessary.
- Use `get_links(direction='in'|'out'|'both')` for graph navigation.
- For project work, call `list_projects` or `get_project_context` and use the returned project identifier. Do not synthesize a project path from a repository owner/name.

## Focused creation and project documents

Use the narrowest tool that represents the intended artifact:

| Intent | Preferred tool |
|---|---|
| Normal note | `create_regular_note` |
| Atomic permanent note | `create_zettel` |
| Literature note | `create_literature_note` |
| Map of content | `create_moc` |
| Managed folder index | `create_folder_readme` |
| Engineering requirements/design | `create_engineering_spec` |
| Implementation plan | `create_implementation_plan` |
| Architecture decision | `record_adr` |
| Reusable bug record | `record_bug` |
| Deferred work | `add_backlog_item` |
| Durable project knowledge | `save_project_knowledge` |

Use `edit_note` for body changes and `update_frontmatter` for supported status, type, and tag changes. Preserve existing human-authored text and unknown frontmatter fields.

Tasks are native Markdown checkboxes. Call `list_tasks` first, then use the returned note and line number with `set_task_state`; do not reuse stale line numbers after editing the note.

## Engineering specs and plans

Kioku keeps a durable distinction between a spec and an implementation plan:

- a **spec** records what is being built and how it must behave;
- a **plan** records how an approved/current design will be implemented in the current codebase.

New project scaffolds create `decisions`, `bugs`, `specs`, `plans`, `knowledge`, `sessions`, and `backlog` as core/eager folders. `daily` and `tickets` remain supported optional/lazy workflows and materialize only on an explicit write.

Spec lifecycle values are `draft`, `approved`, `superseded`, and `discarded`. Use `get_project_context(types='spec')` or `types='specs'` to recover specs; approved specs are current requirements, drafts are in progress, and superseded/discarded specs are historical.

When a plan implements a spec, pass the canonical same-project spec basename returned by Kioku through `create_implementation_plan(spec='SPEC-...')`. Kioku stores the relation in plan frontmatter. Approved specs are the normal execution source; draft links produce a provisional warning; superseded/discarded specs are rejected for new plans.

Prefer the canonical identity returned by Kioku rather than reconstructing filenames. Exact generated spec basenames round-trip even when they contain literal `#`, dots, internal `..`, or a title ending in `.md`.

## Guarded writes and concurrency

Kioku can protect writes with compare-and-swap and durable coordination metadata. Use the strongest contract exposed by the current connection:

1. Read the current note, project document, or work-item projection before mutating it.
2. Pass `expected_revision` or `expected_hash` when the tool exposes those fields and the prior read returned them.
3. Use a stable `mutation_id` for retries of the same logical write; use a new value for a different write.
4. When coordination is enabled and the resource is claimed, pass the current `claim_id`, canonical `resource_key`, and `fence_generation` to the mutation.
5. Treat stale revision, invalid claim, expired lease, or lower fence errors as conflicts to re-read and reconcile. Do not blindly retry with empty preconditions.

For first-class spec creation and spec-linked plan creation, the returned revision identifies the final durable file after optional Templater evaluation. Reusing an already-applied mutation ID does not replay that external Templater side effect.

Empty preconditions preserve legacy write behavior, but they do not provide conflict detection. Direct filesystem edits, Git operations, and edits made outside Kioku do not participate in coordination guarantees.

## Work sessions

For substantial project work, use the focused `kioku-project-workflow` skill or the `project_task` prompt.

- Save the `session_id` returned by `start_work_session`; it is the durable identity for resume and close operations.
- Resume with `start_work_session(session_id='...')` after a client or server restart.
- Close with `end_work_session(session_id='...', summary='...')` whenever the ID is available.
- Use `parent_session_id` only for explicit handoff provenance; it does not transfer claims or authority.
- Do not start a second session when an active session already covers the same goal.
- When `AMBIGUOUS_SESSION` is returned, select one candidate `session_id` instead of retrying without a selector.
- Do not infer lifecycle timestamps from filenames or modification times; Kioku persists UTC timestamps.

MCP prompts return instructions. They do not execute the referenced tools, inspect a repository, or complete a handoff automatically.

## Durable coordination

Call `get_server_capabilities` before coordination tools. Confirm the `kioku.durable-coordination` profile, schema/profile versions, feature flags, transport, observability state, and rollout status.

A safe coordinated mutation normally follows this order:

1. Create or read the work item and its current state version.
2. Acquire the claim for the server-derived resource scope.
3. Transition the work item to the appropriate executing state.
4. Renew the lease during long-running work.
5. Write through Kioku with the current claim, fence, expected content version/hash, and idempotency key.
6. Record completion, partial progress, blocked state, or failure with evidence/reference data.
7. Release the claim when the transition does not already release it.

`agent`, `client_name`, IDs, and trace fields are diagnostic metadata, not authentication or ownership evidence. Coordination is for supported local filesystem sharing; it is not a distributed lock service and does not make cloud-synced folders safe for concurrent writers.

## Destructive and bulk operations

- Use `dry_run=true` or preview/apply separation whenever available.
- Preview permanent deletion, bulk tag changes, attachment cleanup, link application, MOC/folder-index regeneration, and inbox processing.
- Require explicit approval in the current turn before permanent deletion or broad irreversible changes.
- Check `.kioku/config.yml` for exclusions, folder roles, templates, and capability policy before broad vault operations.
- Do not write secrets, private host paths, credentials, or generated embeddings into project documentation.

## Native Git boundary

Kioku does not provide Git tools. For a Git-backed vault, use the client's Git integration separately and inspect the diff before committing. Git does not replace Kioku's path policy, mutation preconditions, or coordination claims. `git restore` is destructive and requires explicit confirmation.

## Full reference

The generated `docs/commands-reference.md` and live MCP discovery are authoritative for names, input/output schemas, annotations, prompts, resources, and current profile counts.
