# Cortex-L7 vault contract

## Repository and configuration

Default vault repository: `sandovaldavid/Cortex-L7`.

Always discover the actual base branch and read `.kioku/config.yml`. The reviewed 2026-07-24 profile is:

```text
projects: 20-execution
knowledge: 30-brain
global sessions: sessions
templates: 99-system/templates
assets: 99-system/attachments
excluded archive: 60-archive
```

Engineering subfolders currently use Kioku defaults: `decisions`, `bugs`, `plans`, `knowledge`, `sessions`, `daily`, `tickets`, and `backlog`.

## Project identifier resolution

Do not use GitHub `owner/repository` as the automatic vault identifier.

Cortex-L7 uses semantic identifiers such as:

```text
yukidoke/yukidoke-api
atena/api.core
fluentreads
```

Resolve an existing project from its MOC and repository evidence. Use the repository leaf only as the fallback for a new standalone project. Nest under a semantic group only when the vault or user provides evidence.

A group note such as `20-execution/yukidoke/yukidoke.md` uses `type: guide`. It is navigation, not a project. Never create loose decisions, plans, bugs, knowledge, sessions, tickets, or backlog notes at a group root.

## Workspace layout

```text
20-execution/
└── <project-identifier>/
    ├── <project-leaf>.md
    ├── decisions/
    ├── bugs/
    ├── plans/
    ├── knowledge/
    ├── sessions/
    ├── daily/
    ├── tickets/
    └── backlog/
```

Respect changed folder mappings from `.kioku/config.yml` instead of hard-coding this snapshot.

## Canonical MOC frontmatter

```yaml
---
tags:
  - moc
  - project
cssclasses:
  - kioku-project-moc
type: moc
status: active
domain: tech
date: YYYY-MM-DD
project: group/project
---
```

Derive `domain` from the longest matching configured folder prefix. Preserve the existing MOC's language, links, formatting, and custom fields.

## Note types, names, and statuses

- decision: `decisions/ADR-NNNN-<title>.md`; `type: decision`; tag `adr`; CSS class `kioku-adr`; use statuses already present such as `proposed`, `accepted`, or `superseded`.
- bug: `bugs/BUG-YYYY-MM-DD-<title>.md`; `type: bug`; CSS class `kioku-bug`; use `open` or `fixed` when supported by evidence.
- plan: `plans/PLAN-YYYY-MM-DD-<title>.md`; `type: plan`; CSS class `kioku-plan`; use `draft`, `active`, or `done` when supported by evidence.
- knowledge: `knowledge/<descriptive-title>.md`; `type: knowledge` unless the existing canonical note uses another established value.
- session: `sessions/YYYY-MM-DD-HHmm-<agent>.md`; `type: session`; tags `session` and `work-log`; CSS class `kioku-session`; use `active`, `blocked`, `waiting`, or `done`.
- ticket: store under `tickets/` using the repository's established ticket naming.
- backlog: store under `backlog/`; `status: proposed`; do not force a date prefix when neighboring notes omit one.
- daily: store under the project `daily/`; use the vault's configured daily template when available.

Use `project:` and `project_link:` consistently with neighboring notes. ADRs may also use `aliases` and `adr:`. Do not introduce a new frontmatter schema across the vault merely to mirror GitHub metadata.

Put source repository, issue, PR, branch, commands, and evidence in a `References`, `Validation`, or `Current state` section unless the target note already uses corresponding frontmatter fields.

## Write policy

- Use one branch per coherent vault update and open a PR to the discovered base branch.
- Prefer `memory/<project-leaf>/<topic-slug>` branch names.
- Update an existing canonical note when the subject already exists.
- Never overwrite historical decisions. Mark superseded decisions and link their replacement.
- Preserve Obsidian wikilinks, callouts, Dataview blocks, YAML style, and the note's primary language.
- Avoid one note per trivial event; preserve durable knowledge, not chat transcripts.
- Do not store credentials, tokens, secrets, personal identifiers, or copied confidential payloads.
- Never modify `.kioku/embeddings.bin`, `.obsidian/`, attachment files, or unrelated Obsidian Git sync changes in an agent-created PR.
- Do not regenerate indexes or binary embeddings through GitHub content writes.
