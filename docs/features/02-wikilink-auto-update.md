# 02 — Auto-actualización de wikilinks en move/rename

> Área: server · Tarea: [P1-02](../tasks/P1-02-wikilink-auto-update.md) · Impacto ★★★ · Esfuerzo M

## Motivación

`move_note` y `rename_note` (`Tools/NoteCommandTools.cs`) hoy **no actualizan los wikilinks
entrantes** — la propia descripción del tool lo declara como limitación de v1. Renombrar una
nota con 20 backlinks rompe 20 enlaces. Es el gap de integridad de datos más citado y Obsidian
sí lo resuelve en su UI, así que el usuario espera lo mismo del agente.

## Diseño

1. Nuevo parámetro `update_links` (default `true`) y `dry_run` (default `false`) en
   `move_note` y `rename_note`.
2. Antes de mover/renombrar, obtener las notas que enlazan al objetivo con el índice de
   backlinks de `VaultIndexService.GetBacklinks(note)`.
3. En cada nota origen, reescribir las variantes de wikilink apuntando al nombre viejo:
   - `[[Nombre]]` → `[[NuevoNombre]]`
   - `[[Nombre|alias]]` → `[[NuevoNombre|alias]]` (el alias se conserva)
   - `[[Nombre#heading]]` / `[[Nombre#^block]]` → se conserva el fragmento
   - `![[Nombre]]` (embeds) → mismo tratamiento
   - Para `move_note` los enlaces por nombre corto no cambian; solo se reescriben los
     enlaces con ruta (`[[Folder/Nombre]]`).
4. La reescritura se hace con un helper nuevo `WikilinkRewriter` en `Services/` (reutiliza
   los patrones de `MarkdownTextExtractor.ExtractWikilinks` para localizar los enlaces, y
   reemplaza por spans — nunca regex global sobre bloques de código).
5. Con `dry_run=true` devuelve el plan: `N enlaces en M notas` con preview por nota.
6. Tras reescribir, reindexar las notas tocadas (`SynchronizeFileReindexAsync`).

Resultado del tool: `[ok] Renamed X to Y — updated N wikilinks in M notes`.

## Casos borde

- **Nombres duplicados** en carpetas distintas: los enlaces por nombre corto son ambiguos.
  Regla: si existe otra nota con el mismo nombre, solo reescribir enlaces con ruta completa
  y reportar los ambiguos en la respuesta (no tocarlos).
- Enlaces dentro de bloques de código o frontmatter: no reescribir (el extractor ya los
  excluye).
- Enlaces markdown estándar `[texto](Nombre.md)`: fuera de alcance v1 del feature
  (documentarlo en la respuesta del tool).

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/NoteCommandTools.cs` (`move_note`, `rename_note`)
- `src/Kioku.Mcp.Server/Services/WikilinkRewriter.cs` (nuevo)
- `src/Kioku.Mcp.Server/Services/MarkdownTextExtractor.cs` (exponer posiciones de wikilinks
  si hace falta)
- `src/Kioku.Mcp.Server.Tests/` — tests de reescritura (alias, heading, embed, ambiguos,
  code blocks) + integración round-trip
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Reescritura incorrecta = corrupción de notas → mitigar con suite de tests exhaustiva,
  `dry_run` y el mecanismo existente de `revert_note` (grupo `restore`).
- Vaults grandes: acotado, los backlinks ya están indexados en memoria.
