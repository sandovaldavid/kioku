# Kioku — Desglose de Tareas

Backlog priorizado del trabajo pendiente. **Cada tarea = una rama desde `origin/develop` +
un PR (squash) hacia `develop`**, siguiendo el workflow del repo (nunca commit directo a
`main`/`develop`). Los specs técnicos detallados viven en [`docs/features/`](../features/README.md).

Convenciones de cada tarea:

- **Rama**: nombre sugerido (`feat/`, `fix/`, `test/`, `chore/`).
- **Commit**: `type(scope): descripción` — scopes válidos: `server | plugin | docs | ci | config | deps | release`.
- **Tamaño**: S (< medio día) · M (1-2 días) · L (> 2 días).
- Checklist común de PR: build + tests verdes, `dotnet format` / `pnpm lint:plugin`, y
  **regenerar `docs/commands-reference.md`** (`dotnet run --project scripts/GenerateCommandsRef`)
  si el PR añade/cambia/renombra tools.

## P0 — Bugs y correcciones (hacer primero)

| ID | Tarea | Rama | Tamaño | Estado |
|----|-------|------|:------:|--------|
| [P0-01](P0-01-suggest-tags-collision.md) | Resolver colisión de nombre `suggest_tags` | `fix/suggest-tags-collision` | S | ✅ Merged (#120) |
| P0-02 | Actualizar `.mcp/server.json` (versión + env vars) | — | S | ✅ Resuelto en el PR de esta revisión de docs |
| [P0-03](P0-03-merge-tools-group.md) | Re-agrupar tools de merge-conflict fuera de `plugin` | `fix/merge-tools-group` | S | ✅ Merged (#121) |
| [P0-04](P0-04-readme-version-sync.md) | Sincronizar versiones de README/server.json con release-please | `chore/readme-version-sync` | S | ✅ Merged (#123) |
| [P0-05](P0-05-add-license.md) | Añadir archivo LICENSE (README lo referencia pero no existe) | `chore/add-license` | S | ✅ Merged (#122) |

## P1 — Alto valor, contenido

| ID | Tarea | Rama | Tamaño | Spec | Estado |
|----|-------|------|:------:|------|--------|
| [P1-01](P1-01-bridge-latent-tools.md) | Exponer 8 comandos latentes del bridge como tools | `feat/bridge-latent-tools` | S | [01](../features/01-bridge-latent-tools.md) | ✅ Merged (#124) |
| [P1-02](P1-02-wikilink-auto-update.md) | Auto-actualizar wikilinks en `move_note`/`rename_note` | `feat/wikilink-auto-update` | M | [02](../features/02-wikilink-auto-update.md) | ✅ Merged (#130) |
| [P1-03](P1-03-plugin-status-ui.md) | Status bar + comandos de control del bridge (plugin) | `feat/plugin-status-ui` | S | [03](../features/03-plugin-status-ui.md) | ✅ Merged (#126) |
| [P1-04](P1-04-bridge-auth-token.md) | Autenticación por token del bridge WebSocket | `feat/bridge-auth-token` | M | [04](../features/04-bridge-auth-token.md) | ✅ Merged (#132) |
| [P1-05](P1-05-http-and-bridge-coverage.md) | Cobertura de tests: HTTP, ApiKeyMiddleware, bridge | `test/http-and-bridge-coverage` | M | — | ✅ Merged (#128) |

## P2 — Horizonte Now (v1.9–2.0)

| ID | Tarea | Rama | Tamaño | Spec | Estado |
|----|-------|------|:------:|------|--------|
| [P2-01](P2-01-local-generation.md) | Generación local con Ollama (`KIOKU_GEN_MODEL`) — **enabler** | `feat/local-generation` | M | [05](../features/05-local-generation.md) | ✅ Merged (#135) |
| [P2-02](P2-02-link-suggestions.md) | Sugerencias de enlaces (`suggest_links` + apply) | `feat/link-suggestions` | M | [06](../features/06-link-suggestions.md) | ✅ Merged (#141) |
| [P2-03](P2-03-daily-digest.md) | Daily digest (`generate_digest`) | `feat/daily-digest` | S | [07](../features/07-daily-digest.md) | ✅ Merged (#137) |
| [P2-04](P2-04-smart-inbox.md) | Smart inbox (`process_inbox`) | `feat/smart-inbox` | S | [08](../features/08-smart-inbox.md) | ✅ Merged (#139) |
| [P2-05](P2-05-mcp-prompts-resources.md) | MCP Prompts & Resources | `feat/mcp-prompts-resources` | M | [09](../features/09-mcp-prompts-resources.md) | ✅ Merged (#143) |

## P3 — Horizonte Next (investigación)

| ID | Tarea | Rama | Tamaño | Spec | Estado |
|----|-------|------|:------:|------|--------|
| [P3-01](P3-01-zotero-bibtex.md) | Import/export BibTeX (base para Zotero) | `feat/zotero-bibtex` | M | [10](../features/10-zotero-bibtex.md) | ✅ Merged (#145) |
| [P3-02](P3-02-flashcards.md) | Flashcards (Spaced Repetition / Anki) | `feat/flashcards` | M | [11](../features/11-flashcards.md) | ✅ Merged (#149) |
| [P3-03](P3-03-incremental-reembedding.md) | Re-embedding incremental (cache v4 + progreso) | `feat/incremental-reembedding` | M | [12](../features/12-incremental-reembedding.md) | Pendiente |
| [P3-04](P3-04-citation-graph.md) | Grafo de citas entre notas y fuentes | `feat/citation-graph` | M | [13](../features/13-citation-graph.md) | ✅ Merged (#147) |

## Dependencias entre tareas

```
P2-01 (generación local) ──► P3-02 (flashcards)
                         └──► mejora P2-03 (digest, opcional)
P1-02 (wikilinks)        ──► mejora P2-04 (smart inbox, opcional)
P2-02 (link suggestions) ──► mejora P2-04 (smart inbox, opcional)
P1-05 (cobertura bridge) ──► recomendado antes de P1-04 (auth cambia el protocolo)
P3-01 (BibTeX)           ──► P3-04 (citation graph usa citekeys)
```

Orden sugerido de ejecución: P0-01 → P0-03 → P0-04 → P1-01 → P1-03 → P1-05 → P1-02 →
P1-04 → P2-01 → P2-03 → P2-04 → P2-02 → P2-05 → P3-*.

## Al completar una tarea

1. Marcar su fila como `✅ Merged (#PR)` en este índice (mismo PR o uno de docs).
2. Si cambió tools: verificar que `commands-reference.md` fue regenerado.
3. Si añadió env vars o grupos de capabilities: actualizar README raíz, README del server,
   `docs/install.md`, `docs/vault-config.md` y `.mcp/server.json` en el mismo PR.
