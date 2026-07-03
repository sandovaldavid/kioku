# P3-02 — Flashcards (Spaced Repetition / Anki)

| Campo | Valor |
|---|---|
| Prioridad | P3 |
| Rama | `feat/flashcards` |
| Commit | `feat(server): add generate_flashcards tool with spaced-repetition and anki output` |
| Tamaño | M |
| Spec | [features/11-flashcards.md](../features/11-flashcards.md) |
| Dependencias | **Requiere [P2-01](P2-01-local-generation.md)** (GenerationService) |

## Objetivo

`generate_flashcards(note, count, format, output_note, dry_run)` en `GenerationTools`:
tarjetas Q/A o cloze generadas localmente, con salida en formato del plugin Spaced
Repetition (`#flashcards`, `Q::A`), CSV para Anki, o cloze.

## Criterios de aceptación

- [ ] JSON del modelo validado con 1 reintento; fallo limpio si no valida
  (`[error] [INTERNAL] model output could not be parsed`).
- [ ] Los 3 formatos renderizan correctamente (tests con servicio mockeado), incluido
  escaping CSV (comas/quotes/saltos de línea).
- [ ] Nota de salida con frontmatter `type: flashcards, source: "[[nota]]"`; `dry_run`
  no escribe.
- [ ] Sin `KIOKU_GEN_MODEL`: `[error] [DEPENDENCY_UNAVAILABLE]` con instrucciones.
- [ ] Prueba manual: tarjetas de una nota real legibles por el plugin Spaced Repetition.
- [ ] `commands-reference.md` regenerado.

## Archivos

- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs`
- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (salida JSON validada)
- Tests + docs
