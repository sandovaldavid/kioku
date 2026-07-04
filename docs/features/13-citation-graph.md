# 13 — Citation graph

> Área: server · Tarea: [P3-04](../tasks/P3-04-citation-graph.md) · Impacto ★★ · Esfuerzo M

## Motivación

Kioku ya sabe *qué* fuentes existen (literature notes con `citekey`, ver
[10 — Zotero/BibTeX](10-zotero-bibtex.md)) y *qué citekeys faltan* (`get_literature_gap`).
Falta la vista inversa: de las fuentes que **sí** tienen una nota, ¿cuáles se están usando
realmente en el resto del vault, y cuáles se importaron y nunca se citaron? Para el persona
investigador esto es la diferencia entre una biblioteca viva y un cementerio de referencias
importadas por inercia.

## Diseño

### `get_citation_graph(folder = "")` en `ResearchTools`

Construye un grafo bipartito **nota-de-trabajo → fuente** a partir de dos señales, combinadas
sin duplicar:

1. **Backlinks** (`VaultIndexService.GetBacklinks(note.Name)`): cualquier nota que enlaza
   `[[Nombre de la Literature Note]]` cuenta como cita.
2. **Citekeys inline** (mismo patrón `[@citekey]` / `@citekey` que ya usa
   `get_literature_gap`, vía `InlineCitePattern`): cualquier nota que menciona `@citekey` en
   su cuerpo, aunque no haya wikilink a la literature note.

Ambas señales se fusionan por nota citante (una nota que hace ambas cosas cuenta una sola
vez). Esto reutiliza exactamente los dos mecanismos de citación que el resto del código ya
reconoce — no se introduce un tercer formato de cita.

**Fuentes** = notas con `citekey` en frontmatter (mismo criterio que `export_citations` /
`export_bibtex`: `ExtraFields["citekey"]`, con fallback a `citation-key` / `key` para no
romper vaults que no usan `import_bibtex`).

**Salida** (texto, mismo estilo que el resto de `ResearchTools`):
- Top fuentes más citadas (citekey, título, conteo de notas citantes), ordenadas descendente.
- Fuentes huérfanas: tienen `citekey` pero cero notas citantes (candidatas a revisar o borrar).
- Notas de trabajo sin ninguna cita a una fuente conocida — *fuera de alcance de esta
  primera versión*: ya lo cubre `get_literature_gap` desde el ángulo opuesto (citekeys
  citados sin nota), y listar "toda nota sin citas" sería ruidoso para notas que
  legítimamente no son de investigación (zettels, tareas, etc.). Se puede añadir después si
  se pide explícitamente.
- Vault sin literature notes (`citekey`): mensaje `[ok]` claro, no error — mismo patrón que
  `export_bibtex`/`export_citations` cuando no encuentran citekeys.

### Parámetro `folder`

Igual que en el resto de `ResearchTools`: si se pasa, solo se consideran fuentes dentro de
ese folder (las notas *citantes* pueden estar en cualquier parte del vault — el filtro es
sobre qué fuentes se reportan, no sobre quién puede citarlas).

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` (+1 tool, reutiliza `InlineCitePattern` ya
  existente en el archivo)
- Tests: fixture con literature notes + notas que las citan (por wikilink y por `@citekey`),
  fuente huérfana, vault sin citekeys
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Doble conteo si una nota cita la misma fuente por wikilink **y** por `@citekey` — mitigado
  fusionando ambas señales en un `HashSet` por nota citante antes de contar.
- Nombres de literature notes con caracteres especiales rompiendo el backlink lookup — ya
  mitigado por `VaultIndexService`'s índice de backlinks existente (usado sin cambios).
