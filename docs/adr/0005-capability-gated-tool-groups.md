# ADR-0005: Capability-gated tool groups

## Status

Accepted (implemented; profile counts tracked live in `docs/commands-reference.md`).

## Context

Kioku's tool surface is large and covers very different risk levels: read-only queries, note
mutation, project/engineering workflows, and higher-risk groups such as bridge control of the
Obsidian UI, CSS snippet injection, asset management, citation/research tooling, and local-LLM
generation. Per `docs/commands-reference.md`'s live MCP-discovery counts, the default profile
exposes 43 tools; enabling every optional group brings that to 59. Every tool registered adds to
the `tools/list` schema payload every MCP client pays for on every session, whether or not it
ever calls those tools, and several groups are destructive or reach outside the vault/Ollama
boundary.

## Decision

Core query, command, and utility tools are always registered. Six optional groups —
`tasks, organization, sessions, workflows, graph, engineering` — are enabled by default. Six more
— `research, generation, css, assets, bridge, plugin` — are disabled by default. A vault-level
`{vault}/.kioku/config.yml` `capabilities:` block controls this per vault: a denylist
(`capabilities.disabled: [...]`) subtracts from the default-enabled set, or, with
`require_explicit: true`, an allowlist (`capabilities.enabled: [...]`) starts from nothing.
Changes require a server restart.

## Alternatives rejected

Exposing all tools unconditionally, with no grouping. Rejected on two measured/documented
grounds:

- **Schema-token cost.** `docs/benchmarks.md`'s schema-cost benchmark measured the default
  profile (43 tools) at ~53,313 bytes (~13,329 estimated tokens) of `tools/list` JSON, versus
  the all-capabilities profile (59 tools) at ~64,753 bytes (~16,189 tokens) — a +2,860 token
  (~21%) delta paid on every session for tools most deployments never call.
- **Blast radius.** `docs/threat-and-privacy-model.md` treats several optional groups as
  meaningfully riskier than the default set: `bridge` reaches the Obsidian UI and third-party
  plugin surface, `css`/`plugin` touch UI-adjacent behavior, and `generation` sends note content
  to a local LLM as a prompt. Gating these off by default is documented as a deliberate way to
  narrow what an AI agent — or a prompt-injected note — can reach without an operator opting in,
  not merely a schema-size optimization.

## Consequences

- Capability changes require a restart; there's no hot toggling of groups mid-session.
- Operators need to understand denylist versus allowlist (`require_explicit`) semantics to reason
  about what's actually enabled for a given vault.
- `docs/commands-reference.md` is the generated source of truth for exact per-profile tool counts
  and schemas, regenerated via `node scripts/generate-public-docs.mjs --write` whenever tool
  groups change.
