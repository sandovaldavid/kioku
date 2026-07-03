# 06 — Sugerencias de enlaces entre notas

> Área: server · Tarea: [P2-02](../tasks/P2-02-link-suggestions.md) · Impacto ★★★ · Esfuerzo M

## Motivación

El valor de un vault es su **grafo**. Ya existen `link_related_notes` (escribe una sección
de relacionados en una nota) y `find_graph_islands`/`find_unlinked_notes` (diagnóstico), pero
no hay un flujo de "propón enlaces y aplícalos con un click" a nivel vault. Es la feature ★★★
del bloque cross-cutting del roadmap.

## Diseño

### `suggest_links(note = "", max_suggestions = 10, min_similarity = 0.7)`

En `GraphAnalysisTools` (grupo `graph-analysis`):

- Con `note`: candidatos por similitud semántica (`HybridSearchService.FindSimilar`) que
  **aún no estén enlazados** en ninguna dirección (filtrar con backlinks + outgoing del
  índice).
- Sin `note` (modo vault): prioriza notas huérfanas (`find_unlinked_notes`) e islas
  (`find_graph_islands`), devolviendo pares `(origen, destino, score, razón)`.
- Salida: lista numerada con score, snippet de contexto y la razón
  (`semantic-similarity` | `orphan-rescue` | `island-bridge`).

### `apply_link_suggestions(note, targets, section = "Relacionados")`

En el mismo grupo:

- `targets`: lista de nombres/rutas (las sugerencias aceptadas por el usuario/agente).
- Añade (o extiende) una sección `## Relacionados` al final de la nota con
  `- [[target]] — razón` por entrada. No toca el cuerpo existente; idempotente (no duplica
  enlaces ya presentes).
- `dry_run` para previsualizar.

Reutiliza la lógica de inserción de `link_related_notes` (extraer helper común si aplica)
en lugar de duplicarla.

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/GraphAnalysisTools.cs` (+2 tools)
- `src/Kioku.Mcp.Server/Services/HybridSearchService.cs` (reuso de `FindSimilar`)
- Posible helper compartido con `ZettelkastenTools.link_related_notes`
- Tests: filtrado de ya-enlazados, idempotencia de apply, huérfanos/islas
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Requiere embeddings (Ollama) — degradar con mensaje claro si `EmbeddingService` no está
  disponible (sin Ollama solo funciona el modo `island-bridge` estructural).
- Sugerencias de baja calidad con `min_similarity` bajo → default conservador (0.7).
