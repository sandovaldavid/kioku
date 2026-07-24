# Cortex-L7 observed profile

This is a reviewed snapshot of `sandovaldavid/Cortex-L7` on 2026-07-24. Always read `.kioku/config.yml` first because the vault may evolve.

## Configured roots

- projects: `20-execution`
- general knowledge: `30-brain`
- global sessions: `sessions`
- templates: `99-system/templates`
- attachments: `99-system/attachments`
- excluded archive: `60-archive`

Engineering subfolders use Kioku defaults: `decisions`, `bugs`, `plans`, `knowledge`, `sessions`, `daily`, `tickets`, and `backlog`.

## Project identity

Project identifiers are semantic vault paths, not GitHub owner/repository paths.

Examples:

- `yukidoke/yukidoke-api`
- `yukidoke/yukidoke-web`
- `atena/api.core`
- `atena/web.admin`
- standalone projects such as `fluentreads`

The project MOC has `type: moc`, `tags: [moc, project]`, `cssclasses: [kioku-project-moc]`, and a `project:` field containing the canonical identifier.

## Group folders

A group such as `yukidoke` or `atena` uses a root note with `type: guide`, not `type: moc`. This prevents Kioku from detecting the group itself as a project and hiding its real subprojects.

Never create project-content subfolders or loose engineering notes at a group root. Cross-repository decisions must be filed in one concrete child project and describe their sibling scope in the note body.

## Search behavior

Cortex-L7 is large and broad GitHub code searches may time out. Prefer narrow lookups based on `.kioku/config.yml`, repository leaf names, project MOCs, known group guides, and recent commit file paths.
