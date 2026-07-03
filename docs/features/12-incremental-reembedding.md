# 12 — Re-embedding incremental

> Área: server · Tarea: [P3-03](../tasks/P3-03-incremental-reembedding.md) · Impacto ★★★ · Esfuerzo M

## Motivación

Tras un arranque con cache inválido (cambio de modelo/dimensión) o en la primera indexación,
Kioku re-embebe **todo** el vault secuencialmente (~60ms/nota local, 2-5s/nota en CPU: para
5000 notas puede ser de minutos a horas). Además el cache no guarda el hash del contenido,
así que no puede distinguir notas cambiadas de notas intactas entre sesiones si el archivo
se tocó sin cambiar contenido.

## Diseño

### 1. Hash de contenido en el cache (formato v4)

- `EmbeddingEntry` incorpora `ContentHash` (el MD5 ya calculado en `Note.ContentHash` —
  cero costo extra).
- `EmbeddingPersistence.FormatVersion` 3 → **4** (invalidación automática del cache viejo,
  una sola re-embebida en la migración; documentarlo en el CHANGELOG del PR).
- En `IndexNoteAsync`: si el hash coincide con el cacheado, **skip** (hoy el criterio de
  frescura vive solo en memoria por sesión).

### 2. Batching y paralelismo controlado

- Cola de re-embedding con paralelismo limitado (p. ej. `SemaphoreSlim(2)`) para no saturar
  Ollama, y flush del cache cada 50 entradas (mecanismo existente).
- Si la API de Ollama del modelo soporta input batch (`/api/embed` con array), usarlo;
  fallback a requests individuales.

### 3. Progreso observable

- `get_index_status` añade: `embedding_backlog` (notas pendientes), `embedded_count`,
  `embedding_rate` (notas/min) y `estimated_remaining`.
- El servidor arranca sirviendo búsquedas keyword mientras el backlog se procesa en
  background (comportamiento actual, ahora medible).

## Archivos afectados

- `src/Kioku.Mcp.Server/Services/EmbeddingPersistence.cs` (formato v4 + hash)
- `src/Kioku.Mcp.Server/Services/EmbeddingService.cs` (skip por hash, cola, contadores)
- `src/Kioku.Mcp.Server/Tools/UtilityTools.cs` (`get_index_status`)
- Tests: round-trip v4, migración v3→v4 (invalidación limpia), skip por hash idéntico
- Docs: `v2-http-sse-spec.md` (formato del cache), `troubleshooting.md` (sección de
  indexación lenta)

## Riesgos

- Cambio de formato binario → la migración debe ser una invalidación limpia, nunca un parse
  corrupto (el magic + version check existente ya lo garantiza).
- Paralelismo contra Ollama en CPU puede degradar la máquina → límite conservador y
  configurable solo si se demuestra necesario.
