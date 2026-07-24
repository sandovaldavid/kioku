---
name: kioku-project-context
description: Loads durable project context from the Cortex-L7 Obsidian vault before GitHub work. Use when resuming a project, resolving an issue, reviewing a PR, auditing documentation, or needing prior decisions, plans, bugs, knowledge, and session handoffs.
license: MIT
compatibility: Requires authenticated GitHub read access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.1.0"
  suite: kioku-chatgpt-skills
---

# Load Kioku Project Context

Read `references/cortex-l7-profile.md`. This skill is read-only: never mutate the source repository or the vault.

## Inputs

- source repository `owner/name`;
- vault repository, default `sandovaldavid/Cortex-L7`;
- optional verified Kioku project identifier;
- optional issue, PR, branch, feature, error, or documentation topic.

## Resolve the project identifier

Do not derive the vault path mechanically from `owner/name`.

1. Discover the vault base branch and read `.kioku/config.yml`.
2. Resolve the configured projects root. Cortex-L7 currently uses `20-execution`.
3. Prefer an explicitly supplied, verified Kioku project identifier.
4. Otherwise locate project MOCs beneath the projects root and match using this evidence order:
   - exact `project:` frontmatter value already associated with the source repository;
   - source repository URL or repository name in the MOC body;
   - exact leaf-name match, such as `yukidoke-api` → `yukidoke/yukidoke-api`;
   - sibling and group-guide context.
5. Treat `type: guide` group notes such as `yukidoke/yukidoke.md` or `atena/atena.md` as navigation only. They are not Kioku projects and must not receive loose decisions, plans, bugs, knowledge, or sessions.
6. When vault code search times out, narrow by repository leaf, configured projects root, known group, and recent commit file paths instead of broad searches.
7. When no project exists, report the proposed identifier. Use the repository leaf as the standalone fallback; nest it under a semantic group only when the vault or user provides evidence for that grouping.
8. When multiple candidates remain plausible, report them and do not guess or publish memory.

## Load context

1. Read the project MOC first (`type: moc`, `project: <identifier>`).
2. Read a parent `type: guide` note only for sibling navigation and cross-repository context.
3. Select context by relevance:
   - `decisions/` for architectural or product constraints;
   - `plans/` for accepted direction and dependencies;
   - `bugs/` for known root causes and regression risks;
   - `knowledge/` for durable implementation knowledge;
   - `tickets/` and `backlog/` for issue-specific context;
   - project `sessions/` for the most recent resumable handoff;
   - project `daily/` only when recent chronology matters.
4. Use the configured global `sessions` folder only for work that has no project.
5. Cross-check current claims against source code, configuration, merged PRs, and issue state. Vault notes explain rationale and history; they do not override current executable reality.
6. Flag stale, superseded, contradictory, duplicated, or unverified notes explicitly.

## Selection limits

Prefer a compact context pack:

- project MOC;
- parent group guide when relevant;
- up to five directly relevant decisions;
- up to three plans or tickets;
- up to three bug or knowledge notes;
- latest relevant session handoff.

Expand only when necessary.

## Output

```text
Project identifier:
Project MOC:
Parent group guide:
Source repository:
Source ref:
Current objective:
Binding decisions:
Relevant plans:
Known bugs and lessons:
Latest handoff:
Contradictions or stale context:
Missing context:
Resolution confidence:
```

Include vault paths or GitHub references for every loaded note. Do not invent content when a workspace is absent.
