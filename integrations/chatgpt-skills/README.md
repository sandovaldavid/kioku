# Kioku ChatGPT Skills

Reusable Agent Skills that reproduce Kioku's project-memory workflow in ChatGPT by using the GitHub connector to read source repositories and publish structured Markdown notes to the private Obsidian vault `sandovaldavid/Cortex-L7`.

## Design

- Source repositories own executable and operational truth: code, configuration, public contracts, current architecture, setup, testing, deployment, and contributor documentation.
- Cortex-L7 owns durable project memory: decisions and alternatives, rationale, cross-repository context, historical status, bug lessons, plans, session handoffs, and private knowledge.
- Every ChatGPT-authored vault mutation uses a dedicated branch and pull request.
- The orchestrator loads project context before work and publishes durable memory afterward.

## Cortex-L7 profile

The suite was reviewed against the real vault configuration on 2026-07-24:

- projects root: `20-execution`;
- general knowledge: `30-brain`;
- global sessions: `sessions`;
- templates: `99-system/templates`;
- engineering subfolders: Kioku defaults;
- semantic project identifiers such as `yukidoke/yukidoke-api` and `atena/api.core`;
- parent group notes use `type: guide` and are never project workspaces.

Skills still read `.kioku/config.yml` on every run so future configuration changes take precedence over this snapshot.

## Skills

| Skill | Purpose |
|---|---|
| `kioku-chat-orchestrator` | Routes a request through project resolution, context loading, a GitHub workflow, and memory publication. |
| `kioku-project-context` | Resolves and reads the real Cortex-L7 project workspace before work begins. |
| `kioku-memory-publisher` | Creates or updates vault-compatible notes through a branch and PR. |
| `kioku-session-handoff` | Records resumable state using Cortex-L7's session schema. |
| `github-issue-resolution` | Resolves a GitHub issue using the supplied quality and evidence protocol. |
| `github-documentation-maintenance` | Audits and updates repository documentation while respecting the repo/vault boundary. |
| `github-pull-request-review` | Reviews PR scope, code, tests, comments, checks, and decision impact. |
| `github-issue-status-sync` | Reconciles issue status with merged PRs, current implementation, blockers, and roadmap state. |
| `github-repo-docs-to-vault` | Classifies existing docs and migrates decision/history material safely. |

## Recommended activation

```text
Use kioku-chat-orchestrator for sandovaldavid/yukidoke-web.
Resolve issue #123 using develop and keep Cortex-L7 updated.
```

The project resolver should select `yukidoke/yukidoke-web`, not construct `sandovaldavid/yukidoke-web` as a vault path.

## Installation

Each immediate child directory containing `SKILL.md` is an independent Agent Skill. Install all nine so the orchestrator can delegate. When Personal Skills are unavailable, use `fallback/PROJECT_INSTRUCTIONS.md` as ChatGPT Project instructions.

## Validation

```bash
python3 scripts/validate_skills.py
```

The validator checks skill frontmatter, manifest dependencies, activation-case JSON, synchronized vault contracts, obsolete path conventions, and recommended size limits.
