# P1-02 — Auto-actualización de wikilinks en move/rename

| Campo | Valor |
|---|---|
| Prioridad | P1 |
| Rama | `feat/wikilink-auto-update` |
| Commit | `feat(server): update inbound wikilinks on move_note and rename_note` |
| Tamaño | M |
| Spec | [features/02-wikilink-auto-update.md](../features/02-wikilink-auto-update.md) |
| Dependencias | Ninguna |

## Objetivo

Que `move_note` y `rename_note` reescriban los wikilinks entrantes (`[[x]]`, `[[x|alias]]`,
`[[x#h]]`, `![[x]]`) usando el índice de backlinks, con `update_links=true` por defecto y
`dry_run` de previsualización. Elimina la limitación declarada de v1.

## Alcance

- Nuevo `Services/WikilinkRewriter.cs` (localización de enlaces reutilizando la lógica de
  `MarkdownTextExtractor`, reemplazo por spans, exclusión de code blocks/frontmatter).
- `NoteCommandTools.move_note` / `rename_note`: params nuevos, reporte
  `updated N wikilinks in M notes`, reindexado de notas tocadas.
- Regla de ambigüedad: si existe otra nota homónima, no tocar enlaces por nombre corto y
  reportarlos.

## Criterios de aceptación

- [ ] Tests de reescritura: nombre simple, alias, heading, block-ref, embed, enlaces en
  code blocks (no tocar), nombres homónimos (no tocar + reportar), rutas completas en move.
- [ ] Test de integración round-trip con `VaultFixture`: rename → backlinks siguen resolviendo.
- [ ] `dry_run=true` no modifica ningún archivo y lista el plan completo.
- [ ] `update_links=false` reproduce el comportamiento actual.
- [ ] `docs/commands-reference.md` regenerado; descripciones de ambos tools ya no declaran
  la limitación.
- [ ] Verificación end-to-end en un vault real: renombrar una nota con varios backlinks y
  comprobar en Obsidian que los enlaces siguen vivos.

## Archivos

- `src/Kioku.Mcp.Server/Services/WikilinkRewriter.cs` (nuevo)
- `src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs`
- `src/Kioku.Mcp.Server/Services/MarkdownTextExtractor.cs` (si hay que exponer posiciones)
- `src/Kioku.Mcp.Server.Tests/WikilinkRewriterTests.cs` (nuevo) + integración
- `docs/commands-reference.md`
