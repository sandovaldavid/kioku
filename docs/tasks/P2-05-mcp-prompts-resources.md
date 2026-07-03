# P2-05 — MCP Prompts & Resources

| Campo | Valor |
|---|---|
| Prioridad | P2 |
| Rama | `feat/mcp-prompts-resources` |
| Commit | `feat(server): expose mcp prompts and note resources` |
| Tamaño | M |
| Spec | [features/09-mcp-prompts-resources.md](../features/09-mcp-prompts-resources.md) |
| Dependencias | Ninguna |

## Objetivo

Exponer las otras dos primitivas MCP con el SDK 1.4.0: resources
(`kioku://note/{path}` por URI template + `kioku://vault/stats`; `resources/list` limitado
a notas recientes) y prompts curados (`research_digest`, `process_inbox`, `weekly_review`,
`literature_review`).

## Criterios de aceptación

- [ ] `resources/list` devuelve un top-N acotado (~20 recientes), no el vault completo.
- [ ] `resources/read` resuelve cualquier nota por URI (y falla con NOT_FOUND limpio).
- [ ] Los 4 prompts aparecen en `prompts/list` con argumentos tipados y se renderizan con
  argumentos de ejemplo.
- [ ] Verificación end-to-end en Claude Code: el prompt aparece como slash command y los
  resources son montables.
- [ ] Spike previo documentado en el PR: qué soporta exactamente `ModelContextProtocol
  1.4.0` (URI templates, subscribe) y qué quedó fuera.
- [ ] Tests de shape + resolución de URI con `VaultFixture`.
- [ ] Docs: sección nueva en README raíz; evaluar extensión de
  `scripts/GenerateCommandsRef` para listar prompts/resources.

## Archivos

- `src/Kioku.Mcp.Server/Prompts/KiokuPrompts.cs` (nuevo)
- Resources según convención del SDK + `Program.cs`
- Tests + docs
