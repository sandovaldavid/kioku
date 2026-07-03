# P3-01 — Import/export BibTeX

| Campo | Valor |
|---|---|
| Prioridad | P3 |
| Rama | `feat/zotero-bibtex` |
| Commit | `feat(server): add bibtex import and export for literature notes` |
| Tamaño | M |
| Spec | [features/10-zotero-bibtex.md](../features/10-zotero-bibtex.md) |
| Dependencias | Ninguna |

## Objetivo

`import_bibtex(source, folder, update_existing, dry_run)` (parser BibTeX propio, literature
notes con `citekey`/`doi` en frontmatter, dedup por citekey) y `export_bibtex(folder)`
(inverso desde las notas con citekey) en `ResearchTools`. La integración con Zotero vivo
queda para una fase 2 vía Better BibTeX auto-export (documentar el flujo).

## Criterios de aceptación

- [ ] Parser tolerante: entradas malformadas se reportan por entrada sin abortar la
  importación; escapes LaTeX comunes (`{\'e}`, `--`) normalizados; campos no reconocidos
  conservados.
- [ ] Dedup: reimportar el mismo `.bib` no crea duplicados; `update_existing=true` refresca
  solo frontmatter.
- [ ] Round-trip: `import_bibtex` → `export_bibtex` conserva las entradas (test).
- [ ] Colisiones de nombre de archivo resueltas con sufijo de citekey.
- [ ] `dry_run` lista crear/saltar/actualizar sin escribir.
- [ ] Tests del parser con fixtures `.bib` reales (unicode, llaves anidadas, comentarios).
- [ ] `commands-reference.md` regenerado + guía corta del flujo Zotero → Better BibTeX →
  Kioku en `docs/` (sección en install.md o doc nuevo).

## Archivos

- `src/Kioku.Mcp.Server/Services/BibtexParser.cs` (nuevo)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs`
- `src/Kioku.Mcp.Server.Tests/BibtexParserTests.cs` (nuevo) + fixtures
- Docs
