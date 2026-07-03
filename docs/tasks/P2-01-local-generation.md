# P2-01 — Generación local con Ollama (enabler)

| Campo | Valor |
|---|---|
| Prioridad | P2 (primera del bloque — desbloquea P2-03 mejorado y P3-02) |
| Rama | `feat/local-generation` |
| Commit | `feat(server): add local text generation service with KIOKU_GEN_MODEL` |
| Tamaño | M |
| Spec | [features/05-local-generation.md](../features/05-local-generation.md) |
| Dependencias | Ninguna |

## Objetivo

`Services/GenerationService.cs` (patrón `EmbeddingService`: init con ping, degradación
graciosa, HttpClient `"ollama"`, JSON source-generated) + env var `KIOKU_GEN_MODEL`
(default deshabilitado) + primer tool `summarize_note` en una clase nueva `GenerationTools`
(grupo de capabilities nuevo `generation`).

## Criterios de aceptación

- [ ] Sin `KIOKU_GEN_MODEL`: el grupo se registra pero `summarize_note` devuelve
  `[error] [DEPENDENCY_UNAVAILABLE]` con instrucciones de setup; el resto del server no
  se ve afectado.
- [ ] Con Ollama + modelo: `summarize_note` devuelve resumen en los 3 estilos
  (`bullets`/`paragraph`/`eli5`) con nota `[info] Generated locally with {model}`.
- [ ] Entrada truncada (~4k chars) y timeout de 120s verificados por test.
- [ ] Grupo `generation` gateable: `capabilities.disabled: [generation]` lo desregistra.
- [ ] Tests con HttpClient mockeado (éxito, timeout, Ollama caído, modelo no descargado).
- [ ] Docs en el mismo PR: env var en README raíz, server README, `install.md`,
  `.mcp/server.json`; grupo nuevo en `vault-config.md` + `vault-config.example.yml`;
  `commands-reference.md` regenerado.

## Archivos

- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (nuevo)
- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (nuevo)
- `src/Kioku.Mcp.Server/KiokuConfiguration.cs`, `Program.cs`,
  `Services/VaultConfigService.cs` (si el gating necesita registro del nombre)
- Tests nuevos + docs listadas
