# 11 — Flashcards / repetición espaciada

> Área: server · Tarea: [P3-02](../tasks/P3-02-flashcards.md) · Impacto ★★★ · Esfuerzo M
> **Depende de:** [05 — Generación local](05-local-generation.md)

## Motivación

Feature ★★★ del persona estudiante: convertir notas en tarjetas Q/A o cloze sin gastar
tokens del agente cloud. Los formatos objetivo ya existen en el ecosistema: el plugin
[Spaced Repetition](https://github.com/st3v3nmw/obsidian-spaced-repetition) (markdown con
`#flashcards` y `Pregunta::Respuesta`) y Anki (import CSV).

## Diseño

### `generate_flashcards(note, count = 10, format = "spaced-repetition", output_note = "", dry_run = false)`

En `GenerationTools` (grupo `generation`, creado por el spec 05):

- Lee el `PlainText` de la nota y pide a `GenerationService` `count` tarjetas en JSON
  estricto (`[{q, a}]` o `[{cloze}]`), con prompt de sistema fijo y validación del parseo
  (reintento 1 vez si el JSON no valida).
- `format`:
  - `spaced-repetition` → bloque markdown con `#flashcards` + `Q::A` (o `¿...?::...`),
    escrito en `output_note` (default: `Flashcards/{nota}.md`) o anexado a la nota fuente
    bajo `## Flashcards`.
  - `anki-csv` → contenido CSV (`front,back,tags`) devuelto en la respuesta y/o escrito a
    un archivo en `folders.assets`.
  - `cloze` → variante cloze del plugin Spaced Repetition.
- `dry_run`: devuelve las tarjetas sin escribir.
- Frontmatter de la nota de salida: `type: flashcards, source: "[[nota]]"`.

Requiere `GenerationService.IsAvailable`; si no, `KiokuError.DependencyUnavailable` con
instrucciones (`KIOKU_GEN_MODEL`).

## Archivos afectados

- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (+1 tool)
- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (helper de salida JSON validada)
- Tests: parsing/validación de tarjetas (mock del servicio), render de los 3 formatos
- `docs/commands-reference.md` (regenerar)

## Riesgos

- Calidad de tarjetas con modelos pequeños → prompt con ejemplos few-shot; el usuario revisa
  antes de estudiar (posicionarlo como borrador).
- JSON malformado del modelo → validación + reintento + degradar a "no se pudo" limpio.
- Escapes CSV (comas/quotes en tarjetas) — test dedicado.
