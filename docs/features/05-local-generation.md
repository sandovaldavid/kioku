# 05 — Generación local con Ollama (enabler)

> Área: server · Tarea: [P2-01](../tasks/P2-01-local-generation.md) · Impacto ★★★ · Esfuerzo M

## Motivación

La tesis de Kioku es descargar trabajo repetitivo de conocimiento a modelos **locales**.
Hoy Ollama solo se usa para embeddings. Añadir un camino de **generación de texto local**
(`summarize`, `explain`, Q/A) multiplica el valor de media docena de features del roadmap:
daily digest (07), flashcards (11), síntesis de literatura, "explícamelo como si tuviera 5".
Es el enabler señalado como "do this first" en
[review/05-feature-roadmap.md](../review/05-feature-roadmap.md).

## Diseño

### `GenerationService` (nuevo, `Services/GenerationService.cs`)

Espejo del patrón de `EmbeddingService`:

- Env var nueva: `KIOKU_GEN_MODEL` (default `""` = **deshabilitado**; ej. `llama3.2`,
  `qwen2.5:3b`). Sin modelo configurado, el servicio reporta `IsAvailable = false` y los
  tools que lo usan devuelven `KiokuError.DependencyUnavailable` con instrucción de setup.
- Endpoint: `POST {KIOKU_OLLAMA_URL}/api/generate` (`stream: false`), HttpClient nombrado
  `"ollama"` existente; timeout propio más generoso (120s — generación en CPU es lenta).
- `InitializeAsync()`: ping a `/api/tags` y verificación de que el modelo está descargado
  (mismo mecanismo que embeddings); degradación graciosa si no.
- API: `Task<string?> GenerateAsync(string prompt, string? system = null, CancellationToken ct)`.
- JSON con source generators (AOT-safe), igual que `OllamaJsonContext` de embeddings.

### Primer tool consumidor (prueba de valor)

`summarize_note(note, style = "bullets", max_words = 150)` en `NoteQueryTools` o una clase
nueva `GenerationTools` (grupo nuevo `generation`, gateado por capabilities como el resto):

- Lee la nota (PlainText del índice), construye prompt con instrucciones de estilo
  (`bullets` | `paragraph` | `eli5`), llama a `GenerateAsync`.
- Responde con el resumen + nota de procedencia (`[info] Generated locally with {model}`).

Grupo `generation` nuevo → añadirlo a `VaultConfigService`/`Program.cs` y documentarlo en
`vault-config.md`.

### Configuración

| Variable | Default | Descripción |
|---|---|---|
| `KIOKU_GEN_MODEL` | — (deshabilitado) | Modelo Ollama para generación local |

## Archivos afectados

- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (nuevo)
- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (nuevo, grupo `generation`)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`, `Program.cs` (DI + registro gateado)
- Tests unitarios (prompt building, degradación) — mock de HttpClient
- Docs: tablas de env vars (README raíz/server, install.md, `.mcp/server.json`),
  `vault-config.md` (grupo nuevo), regenerar `commands-reference.md`

## Riesgos

- **Latencia en CPU**: minutos para notas largas con modelos grandes → truncar entrada
  (~4k chars), documentar modelos recomendados pequeños, timeout claro.
- **Calidad variable**: los tools deben presentar la salida como borrador local, no como
  verdad; el agente cloud siempre puede rehacer el trabajo.
- No enviar nunca contenido a servicios externos: solo `KIOKU_OLLAMA_URL`.
