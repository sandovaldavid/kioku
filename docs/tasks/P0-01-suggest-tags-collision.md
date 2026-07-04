# P0-01 — Resolver colisión de nombre `suggest_tags`

| Campo | Valor |
|---|---|
| Prioridad | P0 |
| Rama | `fix/suggest-tags-collision` |
| Commit | `fix(server): rename duplicate suggest_tags query tool to inspect_note_tags` |
| Tamaño | S |
| Dependencias | Ninguna |

## Contexto

`suggest_tags` está definido **dos veces**:

- `Tools/NoteQueryTools.cs` (core, siempre registrado) — diagnóstico read-only: reporta tags
  actuales/heredados/excluidos de una nota.
- `Tools/VaultOrganizationTools.cs` (grupo `organization`) — sugiere tags nuevos
  (`max_suggestions`).

Cuando el grupo `organization` está habilitado (default), se registran dos tools MCP con el
mismo nombre. Según el cliente/SDK, uno eclipsa al otro o el listado queda ambiguo.

## Alcance

1. Renombrar el de `NoteQueryTools` a **`inspect_note_tags`** (describe mejor su naturaleza
   read-only/diagnóstica). El de `VaultOrganizationTools` conserva `suggest_tags` (es el que
   el nombre promete).
2. Verificar en el código del SDK/registro qué comportamiento tenía la colisión y anotarlo
   en la descripción del PR.
3. Actualizar referencias en docs (README raíz tabla de Consulta, server README).

## Criterios de aceptación

- [ ] `grep -rn '"suggest_tags"\|suggest_tags' src/Kioku.Mcp.Server/Tools/` muestra un único
  tool MCP con ese nombre.
- [ ] Con el grupo `organization` habilitado, `tools/list` no contiene duplicados.
- [ ] Tests de `NoteQueryToolsTests` actualizados y verdes.
- [ ] `docs/commands-reference.md` regenerado (`dotnet run --project scripts/GenerateCommandsRef`).
- [ ] README raíz y `src/Kioku.Mcp.Server/README.md` actualizados.

## Archivos

- `src/Kioku.Mcp.Server/Tools/NoteQueryTools.cs`
- `src/Kioku.Mcp.Server.Tests/NoteQueryToolsTests.cs`
- `docs/commands-reference.md`, `README.md`, `src/Kioku.Mcp.Server/README.md`

## Nota de breaking change

Es un rename de tool visible para agentes: mencionarlo en el cuerpo del PR para que el
CHANGELOG de release-please lo recoja (`fix(server)!:` si se quiere marcar como breaking).
