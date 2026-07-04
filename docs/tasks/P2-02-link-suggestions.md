# P2-02 — Sugerencias de enlaces

| Campo | Valor |
|---|---|
| Prioridad | P2 |
| Rama | `feat/link-suggestions` |
| Commit | `feat(server): add suggest_links and apply_link_suggestions tools` |
| Tamaño | M |
| Spec | [features/06-link-suggestions.md](../features/06-link-suggestions.md) |
| Dependencias | Ninguna dura (usa embeddings existentes); mejora P2-04 |

## Objetivo

`suggest_links(note?, max_suggestions, min_similarity)` (candidatos semánticos no enlazados;
modo vault prioriza huérfanas/islas) y `apply_link_suggestions(note, targets, section)`
(sección `## Relacionados`, idempotente, con `dry_run`) en `GraphAnalysisTools`.

## Criterios de aceptación

- [ ] `suggest_links` nunca propone pares ya enlazados (en cualquier dirección) ni la nota
  consigo misma; salida con score, snippet y razón.
- [ ] Sin Ollama: modo por nota devuelve `[error] [DEPENDENCY_UNAVAILABLE]`; modo vault
  degrada al análisis estructural (huérfanas/islas) con aviso.
- [ ] `apply_link_suggestions` es idempotente (segunda ejecución no duplica) y respeta
  `dry_run`.
- [ ] Reuso verificado: no duplicar la lógica de inserción de `link_related_notes`
  (extraer helper compartido si hace falta).
- [ ] Tests con `VaultFixture` (filtrado, idempotencia, degradación) verdes.
- [ ] `commands-reference.md` regenerado + tablas de README actualizadas.

## Archivos

- `src/Kioku.Mcp.Server/Tools/GraphAnalysisTools.cs`
- `src/Kioku.Mcp.Server/Tools/ZettelkastenTools.cs` (si se extrae helper)
- Tests + docs
