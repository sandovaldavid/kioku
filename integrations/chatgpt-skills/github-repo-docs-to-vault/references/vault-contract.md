# Cortex-L7 vault contract

## Repository

Default vault repository: `sandovaldavid/Cortex-L7`.

Always discover the repository's actual default/base branch. Do not assume `main` or `develop`.

## Project identifier

Use the canonical source repository full name as the project identifier whenever possible:

```text
owner/repository
```

Project identifiers may contain `/`. The leaf repository name is used for the project MOC filename.

## Workspace layout

```text
Projects/
└── owner/
    └── repository/
        ├── repository.md
        ├── decisions/
        ├── bugs/
        ├── plans/
        ├── knowledge/
        ├── sessions/
        ├── daily/
        ├── tickets/
        └── backlog/
```

Respect `.kioku/config.yml` if the vault defines different folder or engineering-subfolder mappings.

## Recommended frontmatter

```yaml
---
type: adr
status: accepted
project: owner/repository
source_repo: owner/repository
source_ref: develop
source_issue: 123
source_pr: 456
date: 2026-07-24
updated: 2026-07-24
tags:
  - kioku
  - engineering
  - decision
---
```

Use only fields supported by evidence. Omit unknown issue, PR, or ref values rather than inventing them.

## Note types

- `adr`: key decision, context, alternatives, consequences.
- `bug`: symptom, root cause, fix, related files, regression prevention.
- `plan`: objective, accepted steps, dependencies, status.
- `knowledge`: durable project knowledge that is not a decision.
- `session`: goal, completed work, current state, blockers, next actions.
- `daily`: dated activity summary.
- `ticket`: durable ticket context when issue content alone is insufficient.
- `idea`: uncommitted backlog idea.
- `moc`: project index and navigation note.

## Write policy

- Use one branch per coherent vault update.
- Prefer `memory/<project-leaf>/<topic-slug>` branch names.
- Create a pull request to the discovered base branch.
- Never push directly to the base branch.
- Update an existing canonical note when the subject already exists.
- Avoid one note per trivial event; preserve durable knowledge, not chat transcripts.
- Do not store credentials, tokens, secrets, personal identifiers, or copied confidential payloads.
