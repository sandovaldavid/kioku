# 09 — MCP Prompts & Resources

> Area: server · Task: [P2-05](../tasks/P2-05-mcp-prompts-resources.md) · Impact ★★★ · Effort M

## Motivation

Kioku only exposes **tools**. The MCP protocol has two more primitives that the SDK
(`ModelContextProtocol 1.4.0`) already supports:

- **Resources** — the client can mount notes as context without spending a tool call.
- **Prompts** — curated workflows that any MCP client (Claude Code, Cursor, VS Code)
  shows as native slash commands.

It's the cheapest distribution channel: packaged workflows appear automatically in
every client.

## Design

### Resources (`[McpServerResource]`)

- `kioku://note/{vault-relative-path}` — a note's content (URI template).
- `kioku://vault/stats` — a vault snapshot (equivalent to `get_vault_stats`).
- **Don't** list all ~5000 notes as static resources: use *resource templates* for
  URI-based resolution and cap `resources/list` to the top-N recent notes (e.g. 20,
  via `VaultIndexService`) so client pickers don't get flooded.

### Prompts (`[McpServerPrompt]`)

First set (new `KiokuPrompts` class):

| Prompt | Arguments | Content |
|---|---|---|
| `research_digest` | `folder?` | Instructions to summarize recent reading with `get_recent_activity` + `search_notes_semantic`, listing open questions |
| `process_inbox` | `inbox?` | Guide for the smart-inbox flow (spec 08): propose → confirm → apply |
| `weekly_review` | — | Weekly review: digest + overdue tasks + orphans + link suggestions |
| `literature_review` | `topic` | Gather evidence via hybrid search and synthesize with `[[wikilink]]` citations |

Prompts reference existing tools by name — keep them in sync with
`commands-reference.md`.

## Affected files

- `src/Kioku.Mcp.Server/Prompts/KiokuPrompts.cs` (new)
- `src/Kioku.Mcp.Server/Resources/` or `Tools/` (resources; per SDK convention)
- `src/Kioku.Mcp.Server/Program.cs` (`.WithPrompts<>()` / `.WithResources<>()`)
- Tests: prompt/resource shape; URI template resolution with a vault fixture
- Docs: new section in the root README + `commands-reference.md` (evaluate whether the
  `scripts/GenerateCommandsRef` generator should cover prompts/resources)

## Risks

- Verify the exact resource template / subscribe support in `ModelContextProtocol
  1.4.0` (if `subscribe` isn't available, ship without change notifications).
- Prompts are hand-maintained text — risk of drift with the tools; mitigate by adding
  a check to the commands-reference generator.
