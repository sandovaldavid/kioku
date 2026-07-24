# Kioku ChatGPT Skills

Reusable Agent Skills that reproduce Kioku's project-memory workflow in ChatGPT by using the GitHub connector to read source repositories and publish structured Markdown notes to an Obsidian vault stored in `sandovaldavid/Cortex-L7`.

## Design

The suite separates responsibilities:

- Source repositories own executable truth: code, configuration, public contracts, current architecture, setup, testing, deployment, and operational documentation.
- Cortex-L7 owns durable project memory: decisions and alternatives, rationale, cross-repository context, historical status, working notes, session handoffs, private knowledge, and future planning.
- Every vault mutation is proposed through a dedicated branch and pull request. Skills must never write directly to the vault's protected base branch.
- The orchestrator loads project context before work and publishes memory after meaningful work.

## Skills

| Skill | Purpose |
|---|---|
| `kioku-chat-orchestrator` | Routes a request through context loading, a GitHub workflow skill, and memory publication. |
| `kioku-project-context` | Reads the relevant Cortex-L7 project workspace before work begins. |
| `kioku-memory-publisher` | Creates or updates Kioku-compatible project notes through a vault PR. |
| `kioku-session-handoff` | Records resumable session state, completed work, blockers, and next actions. |
| `github-issue-resolution` | Resolves a GitHub issue using the supplied quality and evidence protocol. |
| `github-documentation-maintenance` | Audits and updates repository documentation while respecting the repo/vault boundary. |
| `github-pull-request-review` | Reviews PR scope, code, tests, comments, checks, and decision impact. |
| `github-issue-status-sync` | Reconciles issue status with merged PRs, current implementation, blockers, and roadmap state. |
| `github-repo-docs-to-vault` | Classifies existing docs and migrates decision/history material to Cortex-L7 safely. |

## Recommended activation

Use the orchestrator explicitly for end-to-end work:

```text
Use kioku-chat-orchestrator for sandovaldavid/yukidoke-web.
Resolve issue #123 using develop and keep Cortex-L7 updated.
```

The specialist skills may also be invoked directly.

## Installation

Each immediate child directory containing `SKILL.md` is an independent Agent Skill. Install all nine skills so the orchestrator can delegate to them.

ChatGPT Skills follow the Agent Skills open format. In ChatGPT, upload each skill folder from the Skills interface when that feature is available for your plan or workspace. For accounts without Personal Skills access, use `fallback/PROJECT_INSTRUCTIONS.md` as ChatGPT Project instructions and keep the folders version-controlled for future installation.

## Validation

```bash
python3 scripts/validate_skills.py
```

The validator checks required frontmatter, directory/name alignment, description presence, duplicate names, and recommended size limits.
