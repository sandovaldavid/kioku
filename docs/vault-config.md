---
layout: default
title: Vault Configuration Guide
sidebar: true
---

Kioku reads an optional configuration file at `{KIOKU_VAULT_PATH}/.kioku/config.yml`.
Every section is optional. If the file or a section is missing, Kioku uses the defaults below.
Keys use `snake_case`.

A complete annotated example is [`vault-config.example.yml`](vault-config.example.yml).

## `vault` - Identity

```yaml
vault:
  name: "My Vault"
```

Informational only; shown in logs and status output.

## `folders` - Creation locations

Folder roles are vault-relative paths. Structured `create_note` kinds, sessions, engineering
workspaces, and templates use these defaults when the caller does not pass a folder.

```yaml
folders:
  inbox: "Inbox"
  zettel: "Zettelkasten"
  daily: "Daily"
  literature: "Literature"
  sessions: "Sessions"
  templates: "System/Templates"
  assets: "System/Attachments"
  projects: "Projects"
  knowledge: "Knowledge"
```

The `zettel` folder role remains a location convention. `zettelkasten` is not a capability group;
use `create_note` with `kind: zettel`, `literature`, `moc`, or `folder-readme` for structured notes.

## `domains` and `defaults`

`domains` assigns a `domain:` property by folder. Exact folders win over the longest matching
prefix, which wins over `defaults.{type}.domain`.

```yaml
domains:
  "Projects": "work/projects"
  "Research": "academic/research"

defaults:
  note:
    type: capture
    status: inbox
  zettel:
    type: concept
    status: active
  literature:
    type: source
    status: draft
    tags: [source]
```

Each default accepts `type`, `status`, `domain`, and `tags`. Explicit values passed to
`create_note` win.

## `exclude` and `auto_tags`

Dot-folders such as `.obsidian`, `.trash`, and `.kioku` are always excluded. Add folders to keep
them out of search, indexing, and embeddings:

```yaml
exclude:
  - "Archive"

auto_tags:
  inherit:
    "Research": [research]
    "Research/Papers": [research, paper]
  exclude_from_tags: [domain, type, status]
```

Tags from the longest matching folder prefix are inherited. `exclude_from_tags` prevents
frontmatter fields from becoming tags.

## `template_folders`

Maps a destination folder to a vault-relative Markdown template. The file is read on each
creation. This applies to structured `create_note` kinds and can supplement Templater folder
templates.

```yaml
template_folders:
  "Journal/Daily": "Templates/Daily Note.md"
  "Areas/Work/Meetings": "Templates/Meeting.md"
```

For an explicitly selected template, use `create_note` with `template`, or use the plugin-only
`apply_template` tool when Templater evaluation is required.

## `engineering` - Project workspaces

The `engineering` capability provides `create_project_doc`, `get_project_context`, `list_projects`,
and `setup_agent_workflow`. It stores ADRs, bugs, plans, knowledge, sessions, daily notes, tickets,
and backlog ideas under `folders.projects/{project}`:

```
Projects/{project}/
  {project}.md
  decisions/
  bugs/
  plans/
  knowledge/
  sessions/
  daily/
  tickets/
  backlog/
```

Project identifiers may contain `/` for grouped projects, such as `Atena/api.core`. Use the full
identifier returned by `list_projects`. The standard subfolder names are configurable:

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

`setup_agent_workflow` copies the embedded engineering templates to
`{folders.templates}/kioku/`. `manage_templates` with `scope: engineering` can list, read, or
replace those overrides.

## `capabilities` - Tool groups

The server exposes 49 tools across 16 classes. Core query, command, and utility tools are always
registered. With no `capabilities` section, these groups are disabled by default:

```yaml
research, generation, css, assets, bridge, plugin
```

The other optional groups are enabled by default:

```yaml
tasks, organization, sessions, workflows, graph, engineering
```

Git, restore, and zettelkasten are removed groups and are not valid capability names. Use native
Git for repository history and recovery, `manage_trash` for Kioku soft-delete listing/restoration,
and `create_note` for structured note conventions.

```yaml
# Disable additional groups. "*" disables every optional group.
capabilities:
  disabled: [research, generation, css, assets, bridge, plugin]

# Or use allowlist mode. Only listed optional groups are registered.
# capabilities:
#   require_explicit: true
#   enabled: [tasks, organization, engineering]
```

An explicit `disabled` list is applied before `require_explicit`. Changes require a server restart
because tool groups are registered at startup.

## Related docs

- [Installation Guide](install.md)
- [Commands Reference](commands-reference.md) - every implemented tool with parameters
- [Migration Guide](migration-v3.md) - old names and their replacements
- [Troubleshooting](troubleshooting.md)
