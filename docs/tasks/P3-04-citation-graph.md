# P3-04 — Grafo de citas

| Campo | Valor |
|---|---|
| Prioridad | P3 |
| Rama | `feat/citation-graph` |
| Commit | `feat(server): add citation graph analysis for literature notes` |
| Tamaño | M |
| Spec | — (derivado de [review/05-feature-roadmap.md](../review/05-feature-roadmap.md); escribir spec corto en `docs/features/` al iniciar) |
| Dependencias | Mejor después de [P3-01](P3-01-zotero-bibtex.md) (usa `citekey` en frontmatter) |

## Objetivo

Analizar qué notas citan qué fuentes (literature notes con `citekey`): fuentes más citadas,
fuentes huérfanas (importadas y nunca citadas), y notas de trabajo sin respaldo
bibliográfico. Complementa `get_literature_gap`.

## Alcance propuesto

- `get_citation_graph(folder = "")` en `ResearchTools` o `GraphAnalysisTools`: construye el
  bipartito nota↔fuente a partir de backlinks hacia literature notes; salida con top citadas,
  huérfanas y métricas básicas.
- Reuso: índice de backlinks de `VaultIndexService`, frontmatter `citekey`.
- Antes de implementar: escribir `docs/features/13-citation-graph.md` con el diseño fino
  (mismo formato que los specs existentes) y enlazarlo aquí.

## Criterios de aceptación

- [ ] Spec 13 escrito y revisado antes del código.
- [ ] Fuentes citadas/huérfanas correctas con fixture de literature notes + notas que las
  referencian.
- [ ] Sin literature notes → mensaje claro, no error.
- [ ] `commands-reference.md` regenerado.

## Archivos

- `docs/features/13-citation-graph.md` (nuevo, primero)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` o `GraphAnalysisTools.cs`
- Tests + docs
