# 09 — MCP Prompts & Resources

> Área: server · Tarea: [P2-05](../tasks/P2-05-mcp-prompts-resources.md) · Impacto ★★★ · Esfuerzo M

## Motivación

Kioku solo expone **tools**. El protocolo MCP tiene dos primitivas más que el SDK
(`ModelContextProtocol 1.4.0`) ya soporta:

- **Resources** — el cliente puede montar notas como contexto sin gastar un tool-call.
- **Prompts** — workflows curados que cualquier cliente MCP (Claude Code, Cursor, VS Code)
  muestra como slash commands nativos.

Es la vía de distribución más barata: los workflows empaquetados aparecen automáticamente
en todos los clientes.

## Diseño

### Resources (`[McpServerResource]`)

- `kioku://note/{vault-relative-path}` — contenido de una nota (URI template).
- `kioku://vault/stats` — snapshot del vault (equivalente a `get_vault_stats`).
- **No** listar las ~5000 notas como resources estáticos: usar *resource templates* para
  resolución por URI y limitar `resources/list` a un top-N de notas recientes (p. ej. 20,
  vía `VaultIndexService`) para no inundar los pickers de los clientes.

### Prompts (`[McpServerPrompt]`)

Primer set (clase nueva `KiokuPrompts`):

| Prompt | Argumentos | Contenido |
|---|---|---|
| `research_digest` | `folder?` | Instrucciones para resumir lecturas recientes con `get_recent_activity` + `search_notes_semantic`, listando preguntas abiertas |
| `process_inbox` | `inbox?` | Guía del flujo smart-inbox (spec 08): proponer → confirmar → aplicar |
| `weekly_review` | — | Revisión semanal: digest + tareas vencidas + huérfanas + sugerencias de enlaces |
| `literature_review` | `topic` | Recolectar evidencia con búsqueda híbrida y sintetizar con citas `[[wikilink]]` |

Los prompts referencian tools existentes por nombre — mantenerlos sincronizados con
`commands-reference.md`.

## Archivos afectados

- `src/Kioku.Mcp.Server/Prompts/KiokuPrompts.cs` (nuevo)
- `src/Kioku.Mcp.Server/Resources/` o `Tools/` (resources; según convención del SDK)
- `src/Kioku.Mcp.Server/Program.cs` (`.WithPrompts<>()` / `.WithResources<>()`)
- Tests: shape de prompts/resources; resolución de URI template con vault fixture
- Docs: sección nueva en README raíz + `commands-reference.md` (evaluar si el generador
  `scripts/GenerateCommandsRef` debe cubrir prompts/resources)

## Riesgos

- Verificar el soporte exacto de resource templates / subscribe en `ModelContextProtocol
  1.4.0` (si `subscribe` no está disponible, lanzarlo sin notificaciones de cambio).
- Los prompts son texto mantenido a mano — riesgo de deriva con los tools; mitigar
  añadiendo un check al generador de commands-reference.
