# Concurrent work sessions

Kioku work sessions use a durable `session_id` so multiple MCP clients and agents can work on the same vault or project without closing or mutating one another's sessions.

## Session identity

Every newly created session stores these frontmatter fields:

```yaml
session_id: 019c...
project: demo
agent: claude
client_name: claude-code
status: active
started_at: "2026-07-18T20:00:00.0000000Z"
ended_at: "2026-07-18T21:30:00.0000000Z"
parent_session_id: 019b...
run_id: run-01
work_item_id: work-01
attempt_id: attempt-01
```

`session_id` is a UUIDv7 and is the primary identity. Filenames and modification timestamps are presentation and filesystem details; callers must not use them as session identity or start time.

All persisted timestamps are ISO 8601 UTC values.

## Start a session

```text
start_work_session(
  project: "demo",
  agent: "claude",
  goal: "Implement the search feature"
)
```

The response includes a human-readable line and a JSON object:

```text
[ok] Work session started: Projects/demo/sessions/2026-07-18-2000-claude.md
{"action":"started","session_id":"019c...","path":"Projects/demo/sessions/2026-07-18-2000-claude.md",...}
```

Callers should retain `session_id` for later resume and close operations. Concurrent starts use exclusive file creation; when two sessions would have the same historical filename, Kioku adds a short identifier suffix rather than overwriting or resuming another session.

## Resume after a restart

Resume an active session by its durable ID:

```text
start_work_session(session_id: "019c...", project: "demo")
```

The project parameter is optional, but when supplied it must match the persisted project. Resume appends a timestamped marker without changing the original `started_at` value.

## Close a session

Prefer an explicit ID:

```text
end_work_session(
  session_id: "019c...",
  summary: "Implemented and tested the search feature."
)
```

Kioku calculates duration from `started_at`, persists `ended_at`, changes only the intended session to `done`, preserves unrelated frontmatter and manual Markdown edits, and writes the file atomically.

The legacy `session_note` selector remains supported for compatibility.

## Implicit resolution

Calling `end_work_session` without `session_id` or `session_note` is intentionally conservative:

1. Kioku filters active sessions by the optional project.
2. It filters by the current MCP `client_name` and normalized agent identity when available.
3. It proceeds only when exactly one candidate remains.

Zero candidates return `NO_ACTIVE_SESSION`. Multiple candidates return `AMBIGUOUS_SESSION` with actionable candidate records containing `session_id`, path, project, agent, client name, and start timestamp. Callers should select one candidate and retry with its explicit ID.

Kioku never resolves ambiguity by choosing the most recently modified note.

## Agent handoff chains

When one agent starts follow-up work from another session, pass the previous ID:

```text
start_work_session(
  project: "demo",
  agent: "codex",
  parent_session_id: "019b...",
  goal: "Review and optimize Claude's implementation"
)
```

`parent_session_id` records provenance but does not automatically close the parent session.

## Coordination compatibility

Work sessions remain compatibility-only unless a caller explicitly supplies a coordination
context. The coordination capability is disabled by default, and legacy starts, resumes, and
closes do not create work items or add coordination fields. The full linking, precondition,
claim, and lifecycle rules are documented in the [durable coordination
profile](durable-coordination.md).

## Concurrency behavior

- Session creation uses exclusive filesystem creation to prevent filename collisions and overwrites.
- Resume and close operations are serialized per `session_id` within the server process.
- Completed writes use a temporary file and atomic replacement.
- A second close of an already completed session returns `SESSION_ALREADY_CLOSED` instead of appending duplicate end blocks.
- Different session IDs can be closed concurrently.

For exact MCP input schemas, use live `tools/list` or the generated [MCP contract reference](commands-reference.md). Regenerate/verify public contracts with `node scripts/generate-public-docs.mjs --write` and `node scripts/generate-public-docs.mjs --check`.
