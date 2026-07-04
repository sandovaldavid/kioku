# 10 — Zotero / BibTeX bridge

> Área: server · Tarea: [P3-01](../tasks/P3-01-zotero-bibtex.md) · Impacto ★★★ · Esfuerzo M

## Motivación

Para el persona investigador (tesis, papers) la biblioteca vive en Zotero/BibTeX. Hoy
`create_literature_note` es manual, nota por nota. Importar una biblioteca completa con
citation keys en frontmatter convierte a Kioku en el puente vault ↔ gestor de referencias
(feature ★★★ del roadmap de investigación).

## Diseño — fase 1: BibTeX (sin dependencias de red)

### `import_bibtex(source, folder = "", update_existing = false, dry_run = false)`

En `ResearchTools` (grupo `research`):

- `source`: ruta a un `.bib` (dentro o fuera del vault) **o** contenido BibTeX inline.
- Parser BibTeX propio y acotado (entradas `@article/@book/@inproceedings/...`, campos
  `author, title, year, journal, doi, url, abstract`): sin NuGet nuevo si es razonable
  (~200 líneas), estilo `FrontmatterParser`. Manejar llaves anidadas `{...}` y comentarios.
- Por entrada: crea literature note vía la lógica de `create_literature_note`, con
  frontmatter extra `citekey: <key>`, `doi`, `source-url`.
- **Dedup por `citekey`**: si ya existe una nota con ese citekey, saltar (o actualizar
  frontmatter si `update_existing=true`, sin tocar el cuerpo).
- `dry_run`: reporta qué se crearía/saltaría.

### `export_bibtex(folder = "")` (complemento)

Inverso: reconstruye un `.bib` desde las literature notes con `citekey` (complementa
`export_citations`, que hoy exporta markdown).

## Fase 2 (posterior, opcional): Zotero vivo

Integración con **Better BibTeX auto-export** (Zotero exporta el `.bib` a disco y Kioku lo
re-importa — cero código de red) antes que con la API HTTP local de Zotero (`:23119`), que
añade acoplamiento. Documentar el flujo recomendado en la guía.

## Archivos afectados

- `src/Kioku.Mcp.Server/Services/BibtexParser.cs` (nuevo)
- `src/Kioku.Mcp.Server/Tools/ResearchTools.cs` (+2 tools)
- Reuso: `ZettelkastenTools.create_literature_note` internals (extraer helper si aplica),
  `NoteHelpers.SanitizeFileName`
- Tests: parser (entradas malformadas, unicode, llaves anidadas), dedup, round-trip
  import→export
- `docs/commands-reference.md` (regenerar)

## Riesgos

- BibTeX real es sucio (LaTeX escapes `{\'e}`, campos no estándar) → parser tolerante:
  conservar el raw de campos no reconocidos en `ExtraFields`, nunca fallar la importación
  completa por una entrada mala (reportar por entrada).
- Colisiones de nombre de archivo entre papers homónimos → sufijo con citekey.
