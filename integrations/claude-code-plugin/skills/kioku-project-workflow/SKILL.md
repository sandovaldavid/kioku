---
name: kioku-project-workflow
description: Use when a user asks to start, resume, plan, implement, review, debug, or document substantial work for a project managed through the Kioku MCP server.
---

# Kioku project workflow

Use this skill to turn a project request into a deliberate repository-and-vault workflow. Kioku provides project memory, guarded Markdown writes, sessions, and optional coordination. The client remains responsible for repository inspection, code changes, tests, Git operations, and external services.

## Inputs

Extract:

- `project`: the exact Kioku project identifier returned by `list_projects` or stored in the project MOC `project:` field.
- `task`: the requested outcome, constraints, acceptance criteria, repository, and target branch when known.

Do not derive a vault path from `owner/repository`, a similarly named folder, or prior convention. When the identifier is missing or ambiguous, call `list_projects`, inspect the relevant MOC, and resolve the project before writing.

## Workflow

### 1. Discover capabilities and load context

For project work:

1. Confirm the active repository and target branch with the client's repository tools.
2. Call `get_server_capabilities` when optional groups or coordination may be needed.
3. Call `get_project_context(project=...)` before editing code or project notes.
4. Read only relevant context, normally in this order:
   - project MOC and latest session handoff;
   - active plans and open bugs;
   - relevant ADRs, tickets, backlog items, and knowledge notes.
5. Inspect the source repository's current code, tests, contracts, configuration, documentation, issues, and pull requests. Vault context is not a substitute for source evidence.

Respect `.kioku/config.yml` folder roles, exclusions, templates, and capability policy. Do not invent local paths, credentials, or unavailable capability groups.

### 2. Classify the request

Choose the smallest workflow that preserves useful context:

| Request | Default action |
|---|---|
| Explanation, lookup, or status | Read/search only; no session or vault write |
| Multi-step implementation | Start a session; create/update a plan when it helps execution |
| Bug investigation or fix | Start a session; record a bug only when root cause and evidence are reusable |
| Architecture choice | Read existing ADRs; record a proposed ADR before treating the decision as accepted |
| Reusable verified lesson | Save project knowledge after validation |
| Deferred improvement | Add a backlog item rather than expanding scope |
| Documentation synchronization | Separate current public repository truth from private reasoning and handoff context |

Do not create every artifact type. Create only what another agent or maintainer will need.

### 3. Start or resume a session when justified

For substantial implementation, investigation, review, migration, or documentation work:

1. Check current sessions and reuse the matching active session when appropriate.
2. Otherwise call `start_work_session` with the exact project and a concise goal.
3. Save the returned `session_id`; use it for resume and close operations.
4. Use `parent_session_id` only for explicit handoff provenance.

Skip the session lifecycle for read-only answers and trivial isolated edits.

### 4. Use focused engineering tools

Prefer the narrow current contracts:

- `create_implementation_plan` for executable multi-step plans.
- `record_bug` for verified symptoms, root cause, fix, and affected files.
- `record_adr` for architecture decisions, alternatives, and consequences.
- `add_backlog_item` for intentionally deferred work.
- `save_project_knowledge` for durable verified lessons.
- `edit_note` for incremental body updates.
- `update_frontmatter` for supported status, type, and tag changes.
- `list_tasks` before `set_task_state`; never rely on a stale line number.

`create_project_doc` is a compatibility wrapper. Do not use it for new workflows when the focused tool exists.

Keep repository changes and vault changes distinct. Do not modify vault content to conceal a product defect, failing contract, missing test, or repository documentation problem.

### 5. Guard every write

Before a mutation:

1. Read the current resource and capture revision/hash metadata when available.
2. Pass `expected_revision` or `expected_hash` to detect concurrent changes.
3. Use one stable `mutation_id` for retries of the same logical write.
4. If durable coordination is enabled, acquire the correct claim and pass `claim_id`, canonical `resource_key`, and `fence_generation`.
5. Re-read and reconcile on stale revision, invalid/expired claim, or fencing conflicts; do not silently drop preconditions.

Empty preconditions preserve legacy behavior but provide no conflict protection. Direct filesystem and Git edits are outside Kioku's coordination guarantee.

### 6. Coordinate only when the profile is active

Coordination is disabled by default. Before using it, verify the `kioku.durable-coordination` capability profile and rollout state.

For coordinated work:

1. Create/read the work item and current state version.
2. Acquire the server-scoped claim.
3. Transition to the executing state.
4. Renew leases during long-running work.
5. Perform guarded Kioku mutations with current claim/fence/preconditions.
6. Transition to `completed`, `partial`, `blocked`, or `failed` with bounded evidence references.
7. Release claims when required.

Do not treat `agent`, `client_name`, session IDs, run IDs, or trace IDs as authentication or ownership.

### 7. Verify and document

Before declaring completion:

1. Run the relevant build, tests, lint, formatting, generated-contract, security, or packaging checks.
2. Inspect changed files, diffs, statuses, PR checks, and review comments.
3. Classify every control as `Passed`, `Failed`, `Not run`, `Blocked`, or `Not applicable`.
4. Update plan tasks and status only for work actually verified.
5. Record bugs, ADRs, knowledge, or backlog entries only when complete enough for a future agent to act on.
6. Report unavailable tools or optional groups explicitly; absence of CI is not success.

### 8. Close the handoff

If a session was started, call `end_work_session(session_id=...)` with:

- objective and context;
- root cause or decision;
- contracts and files affected;
- code/repository changes;
- vault changes;
- exact verification results;
- data-loss, privacy, compatibility, and rollout risks;
- pull requests and remaining review state;
- the exact next action.

For Git-backed vault work, inspect the vault diff and use a separate reviewable branch/PR when the user requests persistence. Exclude generated embeddings, `.obsidian/`, attachments, credentials, private local paths, and unrelated synchronization noise.

## MCP prompt entry point

When prompts are supported, `project_task(project, task)` supplies the same lifecycle guidance. It does not call tools, inspect code, mutate files, run tests, or close the session automatically.
