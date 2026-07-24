---
name: kioku-project-context
description: Loads durable project context from an Obsidian vault repository before GitHub work. Use when resuming a project, resolving an issue, reviewing a PR, auditing documentation, or needing prior decisions, plans, bugs, knowledge, and session handoffs from Cortex-L7.
license: MIT
compatibility: Requires authenticated GitHub read access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Load Kioku Project Context

Read only. Do not mutate either repository.

## Inputs

- source repository `owner/name`;
- vault repository, default `sandovaldavid/Cortex-L7`;
- optional issue, PR, branch, feature, error, or documentation topic.

## Procedure

1. Discover the vault base branch and read `.kioku/config.yml` when present.
2. Resolve the project workspace using the full source repository name. Also search by leaf repository name only when the canonical path is absent.
3. Read the project MOC first.
4. Select context by relevance:
   - `decisions/` for architectural or product constraints;
   - `plans/` for accepted direction and dependencies;
   - `bugs/` for known root causes and regression risks;
   - `knowledge/` for durable implementation knowledge;
   - `tickets/` and `backlog/` for issue-specific context;
   - `sessions/` for the most recent resumable handoff;
   - `daily/` only when recent chronology matters.
5. Cross-check current claims against source code, configuration, merged PRs, and issue state. A vault note explains rationale and history; it does not override current executable reality.
6. Flag stale, superseded, contradictory, or unverified notes explicitly.

## Selection limits

Prefer a compact context pack:

- project MOC;
- up to five directly relevant decisions;
- up to three plans or tickets;
- up to three bug/knowledge notes;
- latest relevant session handoff.

Expand only when necessary.

## Output

Return:

```text
Project:
Source repository:
Source ref:
Vault workspace:
Current objective:
Binding decisions:
Relevant plans:
Known bugs and lessons:
Latest handoff:
Contradictions or stale context:
Missing context:
```

Include source paths or GitHub references for every loaded note. Do not invent content when the project workspace is absent.
