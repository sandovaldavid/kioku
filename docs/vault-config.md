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
`add_backlog_item`, `get_project_context`, `list_projects`, `setup_agent_workflow`,
`list_engineering_templates`, `get_engineering_template`, `set_engineering_template`)
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

**Grouping projects.** A project identifier can use `/` to nest projects under shared folders,
e.g. `record_adr(project: "Atena/api.core", ...)` and `..."Atena/api.common"` scaffold:

```
Projects/Atena/
  api.core/
    api.core.md   # MOC named after the leaf segment, not the full identifier
    decisions/ bugs/ plans/ ...
  api.common/
    api.common.md
    decisions/ bugs/ plans/ ...
```

A folder counts as a project once it has its own `{leaf}.md` MOC note (`type: moc`) or at
least one of the standard subfolders; `Atena/` itself has neither, so it's a pure grouping
folder — `list_projects` recurses through it but never lists it as a project itself. Pass the
full identifier shown by `list_projects` (`"Atena/api.core"`) to every other engineering tool.
Nesting can be arbitrarily deep; only `/` is a group separator — backslashes, `..`, and empty
segments (leading/trailing/double slashes) are rejected.

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

**Template syntax.** These templates use `{{variable}}` placeholders, evaluated by the
server so they work headless (no Obsidian required). Besides the built-ins (`{{date}}`,
`{{time}}`, `{{title}}`, `{{uid}}`, ...), each type receives its own variables:
`{{project}}` everywhere; `{{number}}`, `{{context}}`, `{{decision}}`, `{{consequences}}`,
`{{alternatives}}` (adr); `{{symptom}}`, `{{root_cause}}`, `{{fix}}`, `{{related_files}}`
(bug); `{{objective}}`, `{{steps}}`, `{{ticket}}` (plan); `{{content}}` (knowledge);
`{{description}}` (idea); `{{goal}}`, `{{agent}}` (session); and the project MOC gets
`{{project_folder}}`, `{{decisions_folder}}`, `{{plans_folder}}`, `{{bugs_folder}}`,
`{{backlog_folder}}` for its Dataview blocks. Unknown placeholders are left as-is.

**Templater interop.** [Templater](https://github.com/SilentVoid13/Templater) is JavaScript that
only runs inside Obsidian, while these documents are created by agents that may run headless —
so the embedded default templates shipped by the server stay `{{var}}`-only, guaranteeing the
tool group works with Obsidian closed and Templater not installed. Your own vault override (in
`{templates}/kioku/{typeKey}.md`, or a template passed to `create_note_from_template`) can mix
in Templater syntax (`<% tp.* %>`) on top of the `{{var}}` placeholders the agent fills with
data: the server substitutes `{{var}}` first, writes the note, then — if the resulting content
still contains `<% %>` and Obsidian is open with the Kioku MCP plugin and Templater installed —
asks the real Templater plugin to evaluate the file in place via the bridge. This applies to
`record_adr`, `log_bug`, `create_plan`, `add_backlog_item`, `add_knowledge`,
`setup_agent_workflow` (the project MOC), `start_work_session`, and the generic
`create_note_from_template`. When Templater can't be reached (Obsidian closed, plugin missing,
bridge unreachable), note creation still succeeds and the response includes a
`[warning] template contains Templater syntax; left unevaluated (open Obsidian or use {{var}})`
line — the `<% %>` snippet is left untouched in the file rather than silently dropped or
corrupted. For human-triggered, on-demand evaluation of an arbitrary template file, use the
`apply_template` tool instead.

The default project MOC uses [Dataview](https://blacksmithgu.github.io/obsidian-dataview/)
code blocks to auto-list ADRs, active plans, open bugs, and backlog ideas. Without the
Dataview plugin they render as plain code blocks — replace them with manual lists if you
prefer.

**Managing engineering templates.** Three tools manage the `{templates}/kioku/*.md` overrides
directly, so you can ask an agent to create or tweak them without hand-editing files:
`list_engineering_templates` (which doc types have an override vs. the embedded default, and
which `{{var}}` each supports), `get_engineering_template(type_key)` (read the current effective
body before proposing an edit), and `set_engineering_template(type_key, content,
reset_to_default)` (write or overwrite the override; `reset_to_default=true` deletes it,
reverting to the embedded default). Supported variables per type:

| Type key | Variables (besides the built-ins `date`, `time`, `datetime`, `year`, `month`, `day`, `uid`, `title`) |
|---|---|
| `adr` | `project`, `number`, `context`, `decision`, `consequences`, `alternatives` |
| `bug` | `project`, `symptom`, `root_cause`, `fix`, `related_files` |
| `plan` | `project`, `objective`, `steps`, `ticket` |
| `knowledge` | `project`, `content` |
| `idea` | `project`, `description` |
| `session` | `project`, `goal`, `agent` |
| `daily` | `project` |
| `ticket` | `project` |
| `project-moc` | `project`, `project_folder`, `decisions_folder`, `plans_folder`, `bugs_folder`, `backlog_folder` |

These tools only write the *template*; they never trigger Templater evaluation — a template's
`<% %>` syntax is only evaluated later, when a *note* is generated from it.

**Frontmatter properties.** Notes created by the engineering tools get, beyond
`tags`/`type`/`status`/`domain`/`date`/`project`, two native Obsidian properties: `aliases` —
only ADRs get one (`ADR-0001`, so `[[ADR-0001]]` works as a short link) — and `cssclasses`, a
`kioku-{type}` class on every doc type (`kioku-adr`, `kioku-bug`, `kioku-plan`, `kioku-idea`,
`kioku-knowledge`, `kioku-session`, `kioku-project-moc`) so a CSS snippet (`css` tool group:
`list_css_snippets`/`apply_css_snippet`) can style each document type differently in Obsidian.

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
