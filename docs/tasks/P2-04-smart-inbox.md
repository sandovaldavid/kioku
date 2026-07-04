# P2-04 — Smart inbox

| Campo | Valor |
|---|---|
| Prioridad | P2 |
| Rama | `feat/smart-inbox` |
| Commit | `feat(server): add process_inbox batch triage tool` |
| Tamaño | S |
| Spec | [features/08-smart-inbox.md](../features/08-smart-inbox.md) |
| Dependencias | Ninguna dura; mejor UX con P1-02 (wikilinks) y P2-02 (apply de enlaces) |

## Objetivo

`process_inbox(inbox_folder, max_notes, apply = false)` en `VaultOrganizationTools`: para
cada nota del inbox propone carpeta (`FolderRanker`), tags (herencia + similares) y enlaces
(top-3 semánticos); con `apply=true` ejecuta el plan completo (mover + tags + relacionados).

## Criterios de aceptación

- [ ] `apply=false` (default) no modifica nada y devuelve el plan numerado por nota.
- [ ] `apply=true` ejecuta y reporta por nota qué hizo; las notas movidas conservan
  frontmatter y contenido; con P1-02 mergeado, los wikilinks entrantes se actualizan.
- [ ] Sin Ollama: carpeta/tags funcionan (token overlap), enlaces se omiten con aviso.
- [ ] Inbox vacío / carpeta inexistente → mensajes claros, no errores.
- [ ] La salida recuerda los mecanismos de reversa (`revert_all_uncommitted`, git).
- [ ] Tests con `VaultFixture` (plan, apply, degradación) + `commands-reference.md`
  regenerado.

## Archivos

- `src/Kioku.Mcp.Server/Tools/VaultOrganizationTools.cs`
- Reuso: `FolderRanker`, `NoteHelpers`, `HybridSearchService`
- Tests + docs
