# P3-03 — Re-embedding incremental

| Campo | Valor |
|---|---|
| Prioridad | P3 |
| Rama | `feat/incremental-reembedding` |
| Commit | `feat(server): incremental re-embedding with content hashes and progress` |
| Tamaño | M |
| Spec | [features/12-incremental-reembedding.md](../features/12-incremental-reembedding.md) |
| Dependencias | Ninguna |

## Objetivo

Cache de embeddings **formato v4** con `ContentHash` por entrada (skip de notas sin
cambios entre sesiones), cola de re-embedding con paralelismo limitado y progreso observable
en `get_index_status` (`embedding_backlog`, `embedding_rate`, `estimated_remaining`).

## Criterios de aceptación

- [ ] Round-trip v4 (save/load) con hashes; cache v3 existente se invalida limpiamente
  (una re-embebida, sin errores de parse) — test de migración.
- [ ] Nota sin cambios de contenido entre reinicios **no** se re-embebe (test con fixture).
- [ ] Backlog grande no bloquea el arranque: keyword search disponible de inmediato,
  embeddings se completan en background (comportamiento actual, ahora con métricas).
- [ ] `get_index_status` refleja backlog y tasa; al terminar, backlog = 0.
- [ ] Paralelismo limitado verificado (no más de N requests concurrentes a Ollama).
- [ ] Docs: `v2-http-sse-spec.md` (formato v4), `troubleshooting.md` (indexación lenta),
  CHANGELOG del PR menciona la invalidación única del cache.

## Archivos

- `src/Kioku.Mcp.Server/Services/EmbeddingPersistence.cs`
- `src/Kioku.Mcp.Server/Services/EmbeddingService.cs`
- `src/Kioku.Mcp.Server/Tools/UtilityTools.cs`
- Tests + docs
