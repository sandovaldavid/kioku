# Análisis del inventario de tools de Kioku y plan de consolidación

## Contexto

Tras mergear el PR #232 a `develop`, el usuario pide un análisis completo del inventario de tools
del servidor MCP: ¿es necesaria esa cantidad? ¿cuántas tools debería tener como máximo un MCP?
¿cuáles conviene eliminar (p. ej. git, que un agente ya sabe usar) o volver genéricas sin perder
funcionalidad? El objetivo de fondo es dejar de sobresaturar el contexto del agente.

**Datos duros medidos en vivo** (servidor real vía `tools/list` por stdio, vault vacío sin config):

- Son **128 tools reales**, no 147 — el "147" de docs/skill contaba también los 19 atributos de
  clase `[McpServerToolType]`. Hay que corregir ese número en `SKILL.md` y `docs/vault-config.md`.
- Las 128 se cargan **todas por defecto** (sin `capabilities:` en config, `IsGroupEnabled`
  devuelve `true` para todo). Costo fijo por sesión: **76.397 chars de JSON ≈ 19.100 tokens**.
- Ya existe la infraestructura para reducir esto (`capabilities.disabled/enabled/require_explicit`
  en `VaultConfigService.cs:160-182`), pero los *defaults* son maximalistas.
- Bug de docs encontrado: el XML doc de `CapabilitiesConfig.Disabled` omite 4 grupos reales
  (`organization`, `plugin`, `graph-analysis`, `restore`) — un usuario siguiendo la doc no puede
  apagarlos.

## ¿Cuántas tools debería tener un servidor MCP?

No hay un máximo oficial en la spec. Referencias prácticas:

- **Anthropic** ("Writing effective tools for agents"): "More tools don't always lead to better
  outcomes" — recomienda pocas tools consolidadas orientadas a workflows (ej. un `schedule_event`
  en vez de `list_users`+`list_events`+`create_event`), porque tools solapadas confunden la
  selección del modelo.
- Setups reales documentados: 5 servidores con 58 tools ≈ 55k tokens antes de la primera
  pregunta. Kioku solo ya mete 19k.
- Clientes: VS Code/Copilot corta en **128 tools totales** (todos los servidores sumados — Kioku
  solo ya lo llena); Cursor recomienda mantenerse bajo ~40-50.
- Regla práctica razonable: **20-40 tools por servidor**, y que cada una sea distinguible por
  nombre+descripción sin leer el schema.

Kioku hoy: 128 tools con clusters enteros de variantes casi idénticas → 3-6x por encima del rango
sano. Meta propuesta: **~50 tools definidas, ~30-35 cargadas por defecto (~5-6k tokens, -70%)**.

## Análisis por veredicto (las 128 tools)

Prioridad: **P1** = núcleo, se queda tal cual · **P2** = se queda pero fusionada/genérica ·
**P3** = niche, se queda solo como grupo opt-in (apagado por defecto) · **P4** = eliminar.

### P4 — Eliminar (31 tools): el agente ya lo hace nativo o es subconjunto exacto de otra

| Tool | Por qué eliminarla | Por qué NO conservarla |
|---|---|---|
| `get_git_status`, `list_git_commits`, `stage_note`, `stage_all`, `unstage_note`, `commit_staged` | Wrappers 1:1 sobre `git` con `WorkingDirectory=VaultPath`. Todo agente de código (Claude Code/OpenCode/Antigravity) ejecuta git nativo mejor (flags completos, diffs, rebase). | El único valor añadido era el re-index, y el `FileSystemWatcher` ya reindexa solo. El caso "cliente sin shell" no es el público de Kioku. |
| `fix_merge_conflicts`, `resolve_merge_conflict` | Grep de `<<<<<<<` + edición — el agente lo hace con sus tools de archivo. | Sin lógica propia que justifique 1.400 chars de schema. |
| `revert_note`, `revert_all_uncommitted`, `restore_note_version` | Wrappers de `git restore [--source]`. | Ídem git. La skill documenta el patrón "usa git en el vault" como red de seguridad. |
| `search_notes`, `search_notes_semantic` | Subconjuntos estrictos de `search_notes_hybrid` (pesos 1/0 y 0/1). | Tres tools de búsqueda confunden la selección; una sola con `mode` cubre todo. |
| `get_note_metadata` | Subconjunto de `read_note` (formato json ya trae metadata). | Se vuelve `read_note(metadata_only=true)`. |
| `find_broken_links` | Código idéntico copiado dentro de `audit_vault`. | `audit_vault` ya lo reporta. |
| `get_vault_stats` | `get_vault_snapshot` se autodefine como su reemplazo. | Duplicado con menos información. |
| `get_recent_activity` | Es la sección "Recently Modified" de `get_work_context`. | Subconjunto literal. |
| `get_session_activity` | Mismo cómputo que hace `end_work_session`; se absorbe como param de `list_work_sessions`. | |
| `find_unlinked_notes`, `find_graph_islands`, `measure_vault_density` | Tres vistas del mismo grafo → secciones de `get_vault_snapshot`/`audit_vault`. | 900 chars de schema para datos que caben en un reporte. |
| `get_knowledge_timeline` | `list_notes` con filtros de fecha + orden lo cubre. | Niche incluso para vaults con `date:` consistente. |
| `get_note_embedding` | Diagnóstico de desarrollo (dims + 8 floats). | Cabe como línea extra en `get_server_status`. |
| `extract_action_items` | `list_tasks` ya lista checkboxes abiertos; crear la nota destino es un `create_note`. | Composición trivial. |
| `generate_digest` | Uno de los schemas más caros del server (1.320 chars) para una composición de `get_work_context` + `list_tasks` + `create_note`. | El agente compone mejor el digest que una plantilla fija. |
| `export_note` | `pandoc`/Markdig lo hace el agente; solo soporta HTML. | |
| `share_as_gist` | `gh gist create` es agent-nativo; además mete un token de GitHub al server sin necesidad. | |
| `create_note_ui` | = `create_note` + `open_note_in_obsidian`. | |
| `reclassify_note` | = `suggest_folder`(top-1) + `move_note`, y además NO reescribe wikilinks (inconsistencia/bug latente). | Eliminar es más seguro que arreglar un composite redundante. |
| `reorder_notes_in_folder`, `list_excalidraw_files`, `get_asset_metadata` | Prefijos numéricos / `find` / `stat` — agent-nativo, niche. | |
| `reload_css_snippets`, `toggle_reading_mode`, `fold_all_headings`, `unfold_all_headings`, `scroll_to_block` | Todas son `trigger_obsidian_command("<id>")` con nombre propio. | La genérica ya existe y es más potente. |

### P2 — Volver genéricas / fusionar (unas 60 tools → 25 genéricas)

| Tool nueva (genérica) | Absorbe | Diseño |
|---|---|---|
| `search_notes` | search_notes, search_notes_semantic, search_notes_hybrid | Param `mode: hybrid (default) \| keyword \| semantic`. Hybrid ya degrada sin Ollama. |
| `read_note` | read_note, get_note_metadata | Param `metadata_only: bool`. |
| `list_notes` | list_notes, filter_notes | Filtros opcionales de frontmatter (tag/status/type/rangos de fecha) + paginación existente. |
| `get_links` | get_backlinks, get_outgoing_links | Param `direction: in \| out \| both`. |
| `create_note` | create_note, create_zettel, create_literature_note, create_moc, create_folder_readme, create_note_from_template | Param `kind: note (default) \| zettel \| literature \| moc \| folder-readme` + `template` opcional. Cada kind aporta solo su convención de nombre/frontmatter/carpeta — hoy son 6 copias del mismo write-path. |
| `edit_note` | update_note_content, append_to_note, prepend_to_note | Param `mode: replace \| append \| prepend`. |
| `update_frontmatter` | update_frontmatter, add_tag, remove_tag | Params `add_tags` / `remove_tags` (add/remove_tag ya delegan en esta). |
| `move_note` | move_note, rename_note | Comparten la maquinaria de rewrite de wikilinks; `new_folder` y/o `new_name`. |
| `list_tasks` | list_tasks, list_tasks_by_tag, list_overdue_tasks | Params `tag`, `overdue_only`. |
| `set_task_state` | complete_task, reopen_task | Param `completed: bool` (ambas llaman a `SetTaskCompletionAsync`). |
| `manage_tags` | normalize_tags, rename_tag_globally, merge_tags | Param `operation: normalize \| rename \| merge` + `dry_run` (mismo rewrite regex las tres). |
| `suggest_tags` | suggest_tags, inspect_note_tags | Un solo reporte: sugerencias + existentes/heredados/excluidos. |
| `create_project_doc` | record_adr, log_bug, create_plan, add_backlog_item, add_knowledge | Param `doc_type: adr \| bug \| plan \| backlog \| knowledge` — las 5 ya pasan por el mismo `CreateDocAsync` privado; solo difieren en prefijo/type/status/tag/template. Las 5 juntas hoy = 5.500 chars de schema. |
| `manage_templates` | list_templates, create_template, get_engineering_template, set_engineering_template, list_engineering_templates | Params `scope: vault \| engineering`, `action: list \| get \| set`. Hoy hay DOS sistemas de plantillas paralelos con superficie duplicada. |
| `suggest_links` | suggest_links, apply_link_suggestions, link_related_notes | Param `apply: bool` (default false = dry-run de sugerencias). |
| `export_citations` | export_citations, export_bibtex | Param `format: bibtex \| markdown` (ya casi duplicadas). |
| `audit_citations` | get_citation_graph, get_literature_gap, validate_research_notes | Un reporte de salud bibliográfica. |
| `tidy_attachments` | normalize_attachment_names, move_attachments_to_folder | Ambas renombran/mueven + reescriben referencias; params `normalize_names`, `target_folder`, `dry_run`. |
| `manage_css_snippets` | apply_css_snippet, list_css_snippets, remove_css_snippet | Param `action: list \| apply \| remove`. |
| `get_obsidian_state` | get_obsidian_status, get_active_note_in_obsidian, get_open_notes_in_obsidian, get_selection_in_obsidian | Un snapshot del estado del UI (barato: hoy son 4 tools 0-param). |
| `open_note_in_obsidian` | open_note_in_obsidian, open_in_split | Param `split: bool`. |
| `edit_in_obsidian` | insert_at_cursor, replace_selection | Param `mode: insert_at_cursor \| replace_selection`. |
| `lint` | lint_note, lint_vault | Param `scope: note \| vault`. |
| `get_server_status` | ping, get_index_status | Health + estado del índice + backlog de embeddings en una. |
| `manage_trash` | list_deleted_notes, restore_note_from_trash | Param `action: list \| restore` (lo único de Restore que NO es git-nativo: `.trash` no está en git). |

### P1 — Se quedan tal cual (~24)

`find_similar_notes`, `delete_note`, `get_project_context`, `list_projects`,
`setup_agent_workflow`, `process_inbox`, `suggest_folder`, `audit_vault` (engordada con
broken-links/islands), `get_vault_snapshot` (engordada con density/unlinked/stats),
`get_concept_map`, `query_dataview` (único sin equivalente CLI), `rebuild_index`,
`import_bibtex`, `apply_template`, `get_installed_plugins`, `find_duplicate_notes`,
`summarize_note`, `generate_flashcards`, `find_orphan_assets`, `trigger_obsidian_command`,
`start_work_session`, `end_work_session`, `get_work_context`, `list_work_sessions`.

### P3 — Grupos que pasan a apagado por defecto

`research` (3), `generation` (2 — el agente ES un LLM; solo valen para flujo offline/Anki),
`css` (1), `assets` (2), `plugin` (4) y `bridge` (5) se quedan en el código pero **apagados por
defecto**; se encienden con `capabilities.enabled` (o los enciende `setup_agent_workflow` si el
usuario lo pide). `git` y `restore` desaparecen como grupos (salvo `manage_trash`, que pasa al
core de writes).

## Superficie final propuesta

| Grupo | Hoy | Propuesto | Cargado por defecto |
|---|---|---|---|
| core query | 13 | 6 (`search_notes`, `read_note`, `list_notes`, `get_links`, `find_similar_notes`, `suggest_tags`) | sí |
| core write | 10 | 6 (`create_note`, `edit_note`, `update_frontmatter`, `move_note`, `delete_note`, `manage_trash`) | sí |
| utilities | 3 | 2 (`get_server_status`, `rebuild_index`) | sí |
| tasks | 5 | 2 | sí |
| organization | 10 | 5 (`manage_tags`, `suggest_folder`, `process_inbox`, `audit_vault`, `find_duplicate_notes`) | sí |
| graph (unifica graph + graph-analysis) | 8 | 3 (`get_vault_snapshot`, `get_concept_map`, `suggest_links`) | sí |
| sessions | 6 | 4 | sí |
| engineering | 11 | 4 (`create_project_doc`, `get_project_context`, `list_projects`, `setup_agent_workflow`) | sí |
| templates (workflows) | 5 | 1 (`manage_templates`) | sí |
| zettelkasten | 5 | 0 (absorbido por `create_note`) | — |
| research | 8 | 3 (`import_bibtex`, `export_citations`, `audit_citations`) | **no** |
| generation | 2 | 2 | **no** |
| restore | 5 | 0 (trash → core; git → nativo) | — |
| git | 8 | **0 (grupo eliminado)** | — |
| css | 4 | 1 | **no** |
| assets | 6 | 2 (`find_orphan_assets`, `tidy_attachments`) | **no** |
| bridge | 14 | 5 (`get_obsidian_state`, `open_note_in_obsidian`, `edit_in_obsidian`, `trigger_obsidian_command`) | **no** |
| plugin | 5 | 4 (`query_dataview`, `apply_template`, `lint`, `get_installed_plugins`) | **no** |
| **Total** | **128** | **~50** | **~33 por defecto** |

Estimación de tokens por sesión por defecto: de ~19.1k a **~5-6k** (33 tools × ~600 chars,
recortando además descripciones verbosas tipo `record_adr`/`process_inbox`). Reducción ~70%.
Con todos los grupos encendidos: ~50 tools ≈ 9-10k tokens (aún -50%).

## Por qué NO eliminar más

- `read_note`/`edit_note`/`create_note` parecen "file ops nativas" pero aportan resolución
  nombre→path, preservación de frontmatter, rewrite de wikilinks, Templater y reindex — un agente
  editando el archivo a mano rompe esas invariantes.
- `query_dataview`, `process_inbox`, `get_project_context`, `import_bibtex`, `audit_vault`:
  lógica real multi-paso sin equivalente shell.
- Las tools de bridge no tienen equivalente fuera del WebSocket a Obsidian.

## Plan de implementación (2 PRs a develop)

### PR 1 — No-breaking, inmediato (bajo riesgo)

1. Corregir "147+" → "128" en `SKILL.md` (canónico + `sync-skill.sh`) y `docs/vault-config.md`.
2. Corregir el XML doc de `CapabilitiesConfig.Disabled` (faltan `organization`, `plugin`,
   `graph-analysis`, `restore`).
3. Añadir a `docs/vault-config.md` y a la skill un **perfil recomendado** de config
   (`capabilities.disabled: [git, css, generation, research, assets, restore]`) con la tabla de
   costo en tokens por grupo medida aquí.
4. Recortar las ~10 descripciones más caras (`record_adr` 1.373 chars, `generate_flashcards`
   1.322, `process_inbox` 1.230…) sin cambiar schemas — solo texto.

### PR 2 — Breaking (release mayor vía Release Please, `feat!:`)

1. Implementar las tools genéricas P2 (tabla de arriba), borrando las absorbidas.
2. Eliminar `GitTools.cs` y los wrappers git de `RestoreTools.cs`; mover trash a
   `NoteCommandTools` como `manage_trash`.
3. Eliminar las P4 restantes.
4. Cambiar defaults: grupos `research/generation/css/assets/bridge/plugin` fuera del default
   (mecanismo ya existente; solo cambia la lista de defaults en `Program.cs`/`VaultConfigService`).
5. Reescribir `SKILL.md` a la nueva superficie + `docs/commands-reference.md` + tabla de
   migración vieja→nueva en `docs/`.
6. Tests: los xUnit existentes se re-apuntan a las tools fusionadas (los servicios subyacentes no
   cambian — la fusión es en la capa de Tools); tests nuevos por cada param discriminador
   (`kind`, `mode`, `operation`, `doc_type`, `scope`).

## Verificación

```bash
dotnet build src/Kioku.Mcp.Server/ && dotnet test src/Kioku.Mcp.Server.Tests/
# medir de nuevo el costo real:
#   tools/list por stdio con vault vacío → objetivo: ≤35 tools, ≤40KB JSON por defecto
bash scripts/sync-skill.sh --check
```

Smoke test en vivo contra Cortex-L7 tras reinstalar el tool global: los flujos de la skill
(buscar, crear zettel vía `create_note kind=zettel`, `create_project_doc doc_type=adr`,
`process_inbox dry_run`) funcionan con la nueva superficie.
