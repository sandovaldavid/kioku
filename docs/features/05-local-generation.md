# 05 — Local generation with Ollama (enabler)

> Area: server · Task: [P2-01](../tasks/P2-01-local-generation.md) · Impact ★★★ · Effort M

## Motivation

Kioku's thesis is offloading repetitive knowledge work to **local** models. Today
Ollama is only used for embeddings. Adding a **local text generation** path
(`summarize`, `explain`, Q/A) multiplies the value of half a dozen roadmap features:
daily digest (07), flashcards (11), literature synthesis, "explain it to me like I'm
5". It's the enabler flagged as "do this first" in
[review/05-feature-roadmap.md](../review/05-feature-roadmap.md).

## Design

### `GenerationService` (new, `Services/GenerationService.cs`)

Mirrors the `EmbeddingService` pattern:

- New env var: `KIOKU_GEN_MODEL` (default `""` = **disabled**; e.g. `llama3.2`,
  `qwen2.5:3b`). With no model configured, the service reports `IsAvailable = false`
  and the tools that use it return `KiokuError.DependencyUnavailable` with setup
  instructions.
- Endpoint: `POST {KIOKU_OLLAMA_URL}/api/generate` (`stream: false`), using the
  existing named HttpClient `"ollama"`; its own more generous timeout (120s —
  CPU generation is slow).
- `InitializeAsync()`: ping `/api/tags` and verify the model is downloaded (same
  mechanism as embeddings); graceful degradation if not.
- API: `Task<string?> GenerateAsync(string prompt, string? system = null, CancellationToken ct)`.
- JSON via source generators (AOT-safe), same as embeddings' `OllamaJsonContext`.

### First consumer tool (proof of value)

`summarize_note(note, style = "bullets", max_words = 150)` in `NoteQueryTools` or a
new `GenerationTools` class (new group `generation`, gated by capabilities like the
rest):

- Reads the note (PlainText from the index), builds a prompt with style instructions
  (`bullets` | `paragraph` | `eli5`), calls `GenerateAsync`.
- Responds with the summary + a provenance note (`[info] Generated locally with {model}`).

New `generation` group → add it to `VaultConfigService`/`Program.cs` and document it
in `vault-config.md`.

### Configuration

| Variable | Default | Description |
|---|---|---|
| `KIOKU_GEN_MODEL` | — (disabled) | Ollama model for local generation |

## Affected files

- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (new)
- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (new, group `generation`)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`, `Program.cs` (DI + gated registration)
- Unit tests (prompt building, degradation) — HttpClient mock
- Docs: env var tables (root/server README, install.md, `.mcp/server.json`),
  `vault-config.md` (new group), regenerate `commands-reference.md`

## Risks

- **CPU latency**: minutes for long notes with large models → truncate input
  (~4k chars), document recommended small models, clear timeout.
- **Variable quality**: tools must present the output as a local draft, not ground
  truth; the cloud agent can always redo the work.
- Never send content to external services: only `KIOKU_OLLAMA_URL`.
