# 08 — Smart inbox

> Área: server · Tarea: [P2-04](../tasks/P2-04-smart-inbox.md) · Impacto ★★ · Esfuerzo S

## Motivación

Procesar el inbox (clasificar capturas: carpeta + tags + enlaces) es el trabajo repetitivo
por excelencia de un second brain. Kioku ya tiene las piezas (`suggest_folder`,
`suggest_tags`, `find_similar_notes`, `FolderRanker`) pero el agente debe orquestarlas nota
por nota, gastando tokens. Un tool que lo haga en batch localmente es la materialización
directa de la tesis del producto.

## Diseño

### `process_inbox(inbox_folder = "", max_notes = 20, apply = false)`

En `VaultOrganizationTools` (grupo `organization`):

- `inbox_folder` default: `folders.inbox` de `.kioku/config.yml` (fallback `"Inbox"`).
- Para cada nota del inbox (hasta `max_notes`):
  1. **Carpeta sugerida** — `FolderRanker.RankFolders` (top-1 + score).
  2. **Tags sugeridos** — lógica de `suggest_tags` (herencia de carpeta destino + similares).
  3. **Enlaces sugeridos** — top-3 de `HybridSearchService.FindSimilar` (si hay embeddings).
- `apply = false` (default): devuelve el **plan** por nota, numerado:
  `1. "Captura X" → Research/Papers · tags: [paper, ml] · links: [[A]], [[B]]`.
- `apply = true`: ejecuta el plan completo — mueve (`move_note` con actualización de
  wikilinks si el spec 02 ya está mergeado), aplica tags (`add_tag`) y añade la sección de
  relacionados (reuso del apply del spec 06). Reporta por nota qué se hizo.

### `apply_inbox_plan(items)`

Variante granular: recibe el subconjunto de índices/notas aceptados del plan previo, para el
flujo "propón todo, aplica solo estos". (Si complica la v1, puede posponerse: `apply=true`
ya cubre el flujo básico.)

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/VaultOrganizationTools.cs` (+1-2 tools)
- Reuso: `FolderRanker`, `NoteHelpers.MergeTagsWithInheritance`, `HybridSearchService`
- Tests: plan correcto con fixture (carpetas/domains de config), apply idempotente
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Mover notas en batch es la operación más destructiva del catálogo → default `apply=false`,
  y recordar en la salida que `revert_all_uncommitted`/git existen (grupos `restore`/`git`).
- Sin Ollama: carpeta/tags siguen funcionando (FolderRanker mezcla token overlap); enlaces se
  omiten con aviso.
- Depende suavemente de los specs 02 y 06 (mejor experiencia), pero no bloquea.
