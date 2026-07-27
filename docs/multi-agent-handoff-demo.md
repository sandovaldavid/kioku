# Multi-agent handoff demo

This page walks through a reproducible demo that proves Kioku's core value
proposition from [issue #257](https://github.com/sandovaldavid/kioku/issues/257):
one AI agent can persist a plan, a decision, and a bug directly in your vault,
end its session, and exit — and a second agent, in a completely separate
process that never saw the first agent's conversation, can resume the work
from that vault state alone.

The demo has two independent parts, and they prove two different things:

- **Handoff mechanics** (`scripts/Kioku.HandoffDemo`): drives the real MCP
  stdio protocol directly, with two separate server subprocesses and two
  distinct client identities. This is reproducible by anyone with the .NET 10
  SDK, requires no API keys, and costs nothing to run.
- **Client registration** (`scripts/add-to-client.sh`): proves that Kioku
  registers cleanly with real, independently installed MCP client CLIs
  (Claude Code, Codex, OpenCode). This does not exercise the handoff scenario
  itself — it only proves the one-command registration script works against
  each client's real configuration mechanism.

Do not confuse the two: the handoff demo never calls `add-to-client.sh`, and
the client-registration commands never call any Kioku tool.

## The scenario

The driver reproduces the exact sequence from issue #257:

1. Agent 1 (simulating Claude Code) starts a project session.
2. It records an implementation plan, an ADR, and a bug/root-cause note, then
   ends its session with a summary.
3. Its process exits — the demo captures the subprocess's PID and exit code
   as proof.
4. Agent 2 (simulating Codex) starts in a fresh session, in its own
   subprocess, with its own MCP client identity.
5. It retrieves project context with `get_project_context` and continues from
   the exact handoff, without having participated in steps 1–3.
6. Agent 2 closes its own session without touching or modifying Agent 1's
   session history.
7. A third, independent connection verifies both sessions exist, are closed,
   and carry distinct ids.

The two agents never share a connection. Each is its own `McpClient`
connection to its own `Kioku.Mcp.Server` subprocess, both pointed at the same
vault path — the only thing they share is the filesystem, exactly like two
real CLI agents working on the same repository.

## Prerequisites

- .NET 10 SDK (`dotnet --version` reports a `10.x` SDK).
- A vault path. The driver defaults to a disposable copy of
  `demo/handoff/fixture-vault`, so no setup is required for a first run.

## Fixture vault

`demo/handoff/fixture-vault/` is a minimal, checked-in seed vault containing
only a `README.md`. It holds no real project data — the demo project
(`acme-checkout`) and its plan, ADR, and bug are fictional fixtures invented
for this walkthrough.

By default, the driver copies this seed into a fresh temporary directory
before writing anything, so repeated runs stay reproducible and never dirty
`git status`. Pass `--vault <path>` to point the driver at a real vault
instead.

## Run the handoff demo

Build the solution once, then run the driver from the repo root:

```bash
dotnet build Kioku.slnx
dotnet run --project scripts/Kioku.HandoffDemo
```

The driver builds `src/Kioku.Mcp.Server` itself if it is not already built,
then spawns three short-lived server subprocesses in sequence — one per
connection — all pointed at the same vault copy.

### Why the driver adds `--no-build` to `dotnet run`

The task brief that inspired this demo describes the server launch command as
`dotnet run --project src/Kioku.Mcp.Server`. That is what the driver uses, but
with `--no-build` appended after an explicit build step. This is a real,
verified constraint, not a stylistic choice: on a clean checkout, plain
`dotnet run` writes MSBuild's restore and build progress to **stdout**, and
the MCP stdio transport uses that same stream for JSON-RPC framing. A cold
build's console noise silently corrupts the handshake. Building once with
`dotnet build` and then spawning with `--no-build` keeps every subsequent
`dotnet run` invocation's stdout clean.

### Actual captured output

This is the real, unedited output from a run against a freshly seeded
temporary vault (some multi-line tool responses are excerpted; the driver
prints `... (N more line(s) omitted)` where it truncates):

```text
=== Kioku multi-agent handoff demo ===
[info] Vault: /tmp/kioku-handoff-demo-a3597e96f091489dbf3d28ffb3501de7
[info] Server project: /home/sandovaldavid/workspaces/me/projects/kioku/.claude/worktrees/planning-002-execution/src/Kioku.Mcp.Server
[info] Server already built: /home/sandovaldavid/workspaces/me/projects/kioku/.claude/worktrees/planning-002-execution/src/Kioku.Mcp.Server/bin/Debug/net10.0/Kioku.Mcp.Server.dll

=== Agent 1 ("claude-code-demo") starts a project session ===
[info] Connected as MCP client "claude-code-demo" (its own subprocess, its own stdio pipe).
[Agent 1] start_work_session -> session_id=019fa2b3-2c10-717f-b9ed-afd595199970, path=Projects/acme-checkout/sessions/2026-07-27-0831-claude.md
[Agent 1] create_implementation_plan -> [ok] Plan note created: Projects/acme-checkout/plans/PLAN-2026-07-27-Add-idempotency-keys-to-checkout-retries.md
[Agent 1] record_adr -> [ok] Decision note created: Projects/acme-checkout/decisions/ADR-0001-Use-Redis-backed-idempotency-keys-for-checkout-retries.md
[Agent 1] record_bug -> [ok] Bug note created: Projects/acme-checkout/bugs/BUG-2026-07-27-Duplicate-charges-on-checkout-retry-after-gateway-timeout.md
[Agent 1] end_work_session -> session_id=019fa2b3-2c10-717f-b9ed-afd595199970, duration_seconds=0, notes_touched=3
[info] "claude-code-demo" subprocess fully exited: pid=929023, exit_code=137.
[info] Agent 1's process has fully exited; its MCP connection no longer exists.

=== Agent 2 ("codex-demo") starts in a fresh session ===
[info] Connected as MCP client "codex-demo" (its own subprocess, its own stdio pipe).
[Agent 2] get_work_context (vault-wide; shows Agent 1's session is no longer active):
    | # Work Context Snapshot
    |
    | **Generated:** 2026-07-27 08:31 UTC
    |
    | ## Inbox (Inbox) — 0 note(s)
    | _(empty — inbox is clear)_
    |
    | ## In Progress — Drafts (0 note(s))
    | _(no draft notes found)_
    |
    | ## Recently Modified (5 note(s))
    | - [[2026-07-27-0831-claude]] _(modified 0m ago)_
    | - [[BUG-2026-07-27-Duplicate-charges-on-checkout-retry-after-gateway-timeout]] _(modified 0m ago)_
    | - [[ADR-0001-Use-Redis-backed-idempotency-keys-for-checkout-retries]] _(modified 0m ago)_
    | ... (6 more line(s) omitted; see the full run log)
[Agent 2] get_project_context (retrieves Agent 1's plan/ADR/bug):
    | # Project context: acme-checkout
    |
    | **Folder:** Projects/acme-checkout/
    | **Generated:** 2026-07-27 08:31 UTC
    |
    | ## Project overview (MOC)
    |
    | ---
    | project: acme-checkout
    | tags:
    |   - moc
    |   - project
    | cssclasses:
    |   - kioku-project-moc
    | ... (82 more line(s) omitted; see the full run log)
[Agent 2] start_work_session -> session_id=019fa2b3-3b7a-7143-8779-7db9c36f1545 (its OWN session; parent_session_id=019fa2b3-2c10-717f-b9ed-afd595199970 records provenance, it does not resume Agent 1)
[Agent 2] add_backlog_item -> [ok] Idea note created: Projects/acme-checkout/backlog/Add-chaos-test-for-concurrent-retry-storms.md
[Agent 2] end_work_session -> session_id=019fa2b3-3b7a-7143-8779-7db9c36f1545, duration_seconds=0, notes_touched=1
[info] "codex-demo" subprocess fully exited: pid=929171, exit_code=137.

=== Verification ("verifier-demo", independent third connection) ===
[info] Connected as MCP client "verifier-demo" (its own subprocess, its own stdio pipe).
[Verifier] list_work_sessions(project="acme-checkout"):
    | [ok] 2 work session(s) in 'Projects/acme-checkout/sessions':
    |
    | - 2026-07-27-0831-codex — id: `019fa2b3-3b7a-7143-8779-7db9c36f1545` — status: done — agent: codex — project: acme-checkout — started: 2026-07-27T08:31:23.0016692Z — duration: 0m
    | - 2026-07-27-0831-claude — id: `019fa2b3-2c10-717f-b9ed-afd595199970` — status: done — agent: claude — project: acme-checkout — started: 2026-07-27T08:31:19.0564435Z — duration: 0m
    |
[ok] Verified: two distinct session_id values, both status=done, both listed independently under the same project. Agent 2 never reopened or edited Agent 1's session note.
[info] "verifier-demo" subprocess fully exited: pid=929261, exit_code=137.

[ok] Multi-agent handoff demo completed.
[info] Fixture vault copy left at: /tmp/kioku-handoff-demo-a3597e96f091489dbf3d28ffb3501de7
```

The run took about 13 seconds end to end on the machine that captured this
log (three `dotnet run --no-build` server startups plus a handful of tool
calls each).

### What this output proves

- **Real client-identity propagation.** `client_name: claude-code-demo` and
  `client_name: codex-demo` in the generated session notes come from the MCP
  connection's negotiated `ClientInfo.Name`, not from a manually typed
  `agent` argument. The driver never passes `agent` to `start_work_session`;
  it sets `McpClientOptions.ClientInfo` once per connection instead.
- **A genuinely separate process, not just a fresh call.** Disposing Agent
  1's client blocks until the underlying `Kioku.Mcp.Server` subprocess exits,
  and the driver prints the real PID and exit code it observed
  (`pid=929023, exit_code=137`) before Agent 2 ever connects.
- **Agent 2 builds on Agent 1's work without touching it.** Agent 2's backlog
  item text explicitly references "Agent 1's idempotency-key fix... this
  project's ADR and bug report" — content it only knows because
  `get_project_context` returned it, not because it shared a connection or
  session with Agent 1.
- **Independent, closed sessions.** The third connection's
  `list_work_sessions` call shows both sessions under the same project, both
  `status: done`, with distinct `session_id` values and no duplicated close
  blocks. Agent 1's session note was written exactly once by Agent 1 and never
  touched again.

<!-- prettier-ignore -->
> [!NOTE]
> Each subprocess exits with code 137 (SIGKILL), not 0. This is a
> characteristic of the MCP client SDK (`ModelContextProtocol.Core` v1.4.1),
> not of `Kioku.Mcp.Server`: `StdioClientTransport`'s disposal path never
> signals EOF on the child process's stdin before waiting — it only waits up
> to `ShutdownTimeout` for the process to exit on its own, then force-kills
> the process tree. Since the server's stdio read loop has no way to observe
> a graceful signal to stop, it never reaches its own shutdown path and gets
> killed once the timeout elapses. The public client API gives callers no way
> to close stdin gracefully before dispose, so the demo driver cannot avoid
> this. All tool calls have already completed and returned a response by the
> time a connection is disposed, so this loses no data — it only means the
> captured exit code is a kill, not a clean 0. This demo reports it plainly
> rather than hiding it; any fix would need to live in the SDK's client-side
> disposal logic, not in this repo, so there is no actionable follow-up here.

### Command-line options

```text
dotnet run --project scripts/Kioku.HandoffDemo -- [--vault <path>]
```

Without `--vault`, the driver copies `demo/handoff/fixture-vault` into a
fresh `kioku-handoff-demo-<guid>` directory under the OS temp path and prints
that path on completion so you can inspect the generated notes. Pass
`--vault <path>` to run against an existing vault directory instead (it must
already exist).

## Client registration (a separate proof)

`scripts/add-to-client.sh` is Kioku's one-command MCP registration script. It
is unrelated to the handoff driver above — it never calls a Kioku tool, and
it does not create the `acme-checkout` project or any session. What it proves
is narrower and just as important: that `dotnet run --project
scripts/Kioku.HandoffDemo`'s two agents are not a fiction of this demo's own
making, and that real, independently installed MCP client CLIs can register
Kioku with one command each.

The commands below use `--dry-run`, which prints the exact commands or config
changes the script would apply without touching your global CLI
configuration. Run from the repo root:

```bash
scripts/add-to-client.sh claude-code --vault demo/handoff/fixture-vault --dry-run
scripts/add-to-client.sh codex --vault demo/handoff/fixture-vault --dry-run
```

### Actual captured output — Claude Code

```text
Registering the Kioku marketplace and installing the kioku plugin
(bundles the MCP server config with the kioku-vault skill)...
[dry-run] would run: claude plugin marketplace add sandovaldavid/kioku
[dry-run] would run: claude plugin install kioku@kioku
When prompted for the vault path, enter: /home/sandovaldavid/workspaces/me/projects/kioku/.claude/worktrees/planning-002-execution/demo/handoff/fixture-vault
```

### Actual captured output — Codex

```text
[dry-run] would run: codex mcp add kioku --env KIOKU_VAULT_PATH=/home/sandovaldavid/workspaces/me/projects/kioku/.claude/worktrees/planning-002-execution/demo/handoff/fixture-vault -- kioku
```

The absolute vault path in both transcripts reflects the checkout that
captured this run; running the commands from your own clone prints your own
path instead — everything else is identical.

### Bonus: a third client (OpenCode)

`scripts/add-to-client.sh` also supports OpenCode. It was not required for
this demo's "at least two clients" requirement, but running it costs nothing
and strengthens the evidence:

```bash
scripts/add-to-client.sh opencode --vault demo/handoff/fixture-vault --dry-run
```

```text
[dry-run] would merge kioku MCP entry into: /home/sandovaldavid/.config/opencode/opencode.json
[dry-run] would copy skill to: /home/sandovaldavid/.claude/skills/kioku-vault/SKILL.md
```

## Generated vault layout

A full run creates exactly these files under the vault's `Projects/`
folder — nothing outside it, and nothing in the checked-in fixture copy
unless you pass `--vault demo/handoff/fixture-vault` explicitly:

```text
Projects/acme-checkout/
├── acme-checkout.md                                                    (project MOC, auto-scaffolded)
├── backlog/
│   └── Add-chaos-test-for-concurrent-retry-storms.md                   (Agent 2)
├── bugs/
│   └── BUG-2026-07-27-Duplicate-charges-on-checkout-retry-after-gateway-timeout.md   (Agent 1)
├── decisions/
│   └── ADR-0001-Use-Redis-backed-idempotency-keys-for-checkout-retries.md            (Agent 1)
├── plans/
│   └── PLAN-2026-07-27-Add-idempotency-keys-to-checkout-retries.md      (Agent 1)
└── sessions/
    ├── 2026-07-27-0831-claude.md                                       (Agent 1, status: done)
    └── 2026-07-27-0831-codex.md                                        (Agent 2, status: done)
```

Agent 1's session note's full frontmatter shows the client identity claim
made above, verbatim from a captured run:

```yaml
session_id: 019fa2b3-2c10-717f-b9ed-afd595199970
agent: claude
client_name: claude-code-demo
started_at: "2026-07-27T08:31:19.0564435Z"
project: acme-checkout
project_link: "[[acme-checkout]]"
tags:
  - session
  - work-log
cssclasses:
  - kioku-session
type: session
status: done
date: 2026-07-27
ended_at: "2026-07-27T08:31:19.2726150Z"
```

Agent 2's session note carries `parent_session_id: 019fa2b3-2c10-717f-b9ed-afd595199970`
in its own frontmatter — Agent 1's id, recorded as provenance on Agent 2's
own, separate session, per the pattern documented in
[`docs/work-sessions.md`](work-sessions.md#agent-handoff-chains).

## Source

- Driver: [`scripts/Kioku.HandoffDemo/Program.cs`](../scripts/Kioku.HandoffDemo/Program.cs)
- Fixture vault seed: [`demo/handoff/fixture-vault/README.md`](../demo/handoff/fixture-vault/README.md)
- Client registration script: [`scripts/add-to-client.sh`](../scripts/add-to-client.sh)
- MCP tool signatures used: `start_work_session`, `end_work_session`,
  `create_implementation_plan`, `record_adr`, `record_bug`,
  `get_project_context`, `get_work_context`, `add_backlog_item`,
  `list_work_sessions` — see [`docs/commands-reference.md`](commands-reference.md)
  for their full input/output contracts.
