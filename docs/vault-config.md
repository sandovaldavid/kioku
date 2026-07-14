---
layout: default
title: Vault Configuration Guide
sidebar: true
---

Kioku reads an optional configuration file at `{KIOKU_VAULT_PATH}/.kioku/config.yml`.
Every section is optional — if the file or a section is missing, Kioku uses the defaults
described below. Keys use `snake_case` (parsed with underscore naming convention).

A complete annotated example lives at [`vault-config.example.yml`](vault-config.example.yml).

## `vault` — Identity

```yaml
vault:
  name: "My Vault"
```

Informational only; shown in logs and status tools.

## `folders` — Where notes are created

Maps a folder *role* to a vault-relative path. Tools like `create_zettel`,
`create_literature_note`, `start_work_session`, `generate_digest` and the template tools
consult this map when the caller does not pass an explicit folder.

```yaml
folders:
  inbox: "Inbox"
  zettel: "Zettelkasten"
  daily: "Daily"
  literature: "Literature"
  sessions: "Sessions"
  templates: "System/Templates"
  assets: "System/Attachments"
  projects: "Projects"     # engineering tool group: per-project workspaces
  knowledge: "Knowledge"   # engineering tool group: general knowledge notes
```

## `domains` — Frontmatter domain by folder

Assigns the `domain:` frontmatter field to notes created inside a folder (or subfolder).
Precedence: exact folder → longest folder prefix → `defaults.{type}.domain`.

```yaml
domains:
  "Projects": "work/projects"
  "Research": "academic/research"
```

## `defaults` — Frontmatter defaults per note type

Values merged into the frontmatter of new notes according to their `type` key.
Explicit values passed in the tool call always win.

```yaml
defaults:
  note:
    type: capture
    status: inbox
  zettel:
    type: concept
    status: active
    domain: tech/general
  literature:
    type: source
    status: draft
    tags: [source]
```

Each entry accepts `type`, `status`, `domain` and `tags`.

## `exclude` — Folders excluded from the index

Dot-folders (`.obsidian`, `.trash`, `.kioku`, ...) are always excluded. Add extra folders
to keep them out of search, indexing and embeddings:

```yaml
exclude:
  - "Archive"
```

## `auto_tags` — Tag inheritance by folder

Notes created under a folder inherit its tags (longest prefix wins). `exclude_from_tags`
lists frontmatter fields that must never be turned into tags (default: `domain`, `type`,
`status`).

```yaml
auto_tags:
  inherit:
    "Research": [research]
    "Research/Papers": [research, paper]
  exclude_from_tags: [domain, type, status]
```

## `templates` — Body templates per note type

Overrides the built-in body used when creating notes of a given type. Supports template
variables such as `{{title}}`, `{{date}}` and `{{uid}}`.

```yaml
templates:
  zettel: |
    ## {{title}}

    - Created: {{date}}
```

## `engineering` — Per-project workspace subfolders

The `engineering` tool group (`record_adr`, `log_bug`, `create_plan`, `add_knowledge`,
`add_backlog_item`, `get_project_context`, `list_projects`, `setup_agent_workflow`)
stores documents in per-project workspaces under `folders.projects`:

```
Projects/{project}/
  {project}.md   # project MOC note
  decisions/     # ADR-0001-{title}.md
  bugs/          # BUG-{date}-{title}.md
  plans/         # PLAN-{date}-{title}.md
  knowledge/     # project-specific knowledge
  sessions/      # {date-time}-{agent}.md work sessions
  daily/         # daily notes
  tickets/       # human-written tickets the agent structures
  backlog/       # future improvement ideas
```

The subfolder names are configurable (values below are the defaults):

```yaml
engineering:
  subfolders:
    decisions: "decisions"
    bugs: "bugs"
    plans: "plans"
    knowledge: "knowledge"
    sessions: "sessions"
    daily: "daily"
    tickets: "tickets"
    backlog: "backlog"
```

Document bodies come from templates. `setup_agent_workflow` copies the built-in defaults
to `{folders.templates}/kioku/{adr,bug,plan,knowledge,idea,session,daily,ticket,project-moc}.md`;
edit them in Obsidian and they override the embedded versions.

## `capabilities` — Enable/disable tool groups

The core groups (`NoteQueryTools`, `NoteCommandTools`, `UtilityTools`) are always
registered. The 16 optional groups can be gated:

| Group | Tool class |
|---|---|
| `tasks` | TaskManagementTools |
| `zettelkasten` | ZettelkastenTools |
| `organization` | VaultOrganizationTools |
| `sessions` | SessionContextTools |
| `workflows` | WorkflowTools |
| `css` | CssThemingTools |
| `graph` | KnowledgeGraphTools |
| `graph-analysis` | GraphAnalysisTools |
| `research` | ResearchTools |
| `bridge` | ObsidianBridgeTools |
| `plugin` | PluginIntegrationTools |
| `git` | GitTools |
| `restore` | RestoreTools |
| `assets` | AssetTools |
| `generation` | GenerationTools — requires `KIOKU_GEN_MODEL` (see [install.md](install.md)) |
| `engineering` | EngineeringWorkflowTools — per-project ADRs, bugs, plans, knowledge, backlog |

Semantics:

- No `capabilities` section → **all groups enabled** (default).
- `disabled` — list of groups to turn off. `"*"` disables every optional group.
- `require_explicit: true` — only groups listed in `enabled` are registered.

```yaml
# Example 1: everything except git and css
capabilities:
  disabled: [git, css]

# Example 2: allowlist mode — only tasks and zettelkasten
capabilities:
  require_explicit: true
  enabled: [tasks, zettelkasten]
```

Changes require a server restart (tool groups are registered at startup).

## Related docs

- [Installation Guide](install.md)
- [Commands Reference](commands-reference.md) — every tool with parameters
- [Troubleshooting](troubleshooting.md)
