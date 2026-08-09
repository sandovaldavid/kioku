---
layout: default
title: Vault Configuration Guide
sidebar: true
---

# Vault configuration

Kioku reads optional vault behavior from `{KIOKU_VAULT_PATH}/.kioku/config.yml`. Every section is optional and keys use `snake_case`. Process settings such as transport, security, concurrency, and integrations are documented in the generated [server configuration reference](configuration-reference.md).

A complete annotated example is available in [`vault-config.example.yml`](vault-config.example.yml).

## Folder roles and defaults

The `vault` section provides an informational name. The `folders` section assigns vault-relative locations for inbox, zettel, daily, literature, sessions, templates, assets, projects, and knowledge. `domains` and `defaults` define metadata applied by structured note creation; explicit tool arguments always win.

## Exclusions, tags, and templates

- `exclude` removes folders from indexing and embeddings. Dot-folders are always excluded.
- `auto_tags.inherit` applies tags from the longest matching folder prefix.
- `auto_tags.exclude_from_tags` prevents selected frontmatter fields from becoming tags.
- `template_folders` maps destination folders to vault-relative Markdown templates.

`audit_vault` treats a closed empty wikilink or embed (`[[]]` or `![[]]`) as a template placeholder only when the source is a known template. Kioku recognizes sources under `folders.templates`, explicit `template_folders` values, Templater folder-template mappings, and Templater's configured `templates_folder`. These occurrences are reported separately from malformed live links. Non-empty malformed links inside templates, empty links outside known templates, and links that traverse outside the vault boundary remain malformed. This classification is read-only and does not change indexing, search, graph resolution, or note mutation behavior.

## Frontmatter and generated indexes

`frontmatter.maintain_updated` defaults to `false`. When enabled, Kioku maintains `updated` or `modified` timestamps while preserving custom typed frontmatter.

`generated_indexes.refresh` accepts `manual` or `on_mutation`. Managed MOCs and folder readmes preserve user text outside their generated section. `rebuild_index` refreshes the in-memory search index only; it does not rewrite Markdown indexes.

## Engineering workspaces

`engineering.subfolders` customizes the project directories used for decisions, bugs, plans, knowledge, sessions, daily notes, tickets, and backlog items. Project identifiers may contain `/`; use the full identifier returned by `list_projects`.

## Capability profiles

Core tools are always registered. Optional groups enabled by default are:

```text
tasks, organization, sessions, workflows, graph, engineering
```

Optional groups disabled by default are:

```text
research, generation, css, assets, bridge, plugin, coordination
```

Use either a denylist:

```yaml
capabilities:
  disabled: [research, generation, css, assets, bridge, plugin, coordination]
```

or explicit allowlist mode:

```yaml
capabilities:
  require_explicit: true
  enabled: [tasks, organization, engineering]
```

Changes require a server restart. The generated [MCP contract reference](commands-reference.md) records exact profile counts and schemas from live protocol discovery.

The coordination group controls coordination tools and resources. The existing session tools remain registered when coordination is disabled so legacy callers continue to work, but a caller that supplies coordination identifiers receives a stable `COORDINATION_DISABLED` error and no session is created or linked.

## Related documentation

- [MCP contract reference](commands-reference.md)
- [Server configuration reference](configuration-reference.md)
- [Installation guide](install.md)
- [Focused-tool migration](focused-tool-migration.md)
- [Versioning policy](versioning.md)
