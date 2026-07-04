# P3-02 — Flashcards (Spaced Repetition / Anki)

| Field | Value |
|---|---|
| Priority | P3 |
| Branch | `feat/flashcards` |
| Commit | `feat(server): add generate_flashcards tool with spaced-repetition and anki output` |
| Size | M |
| Spec | [features/11-flashcards.md](../features/11-flashcards.md) |
| Dependencies | **Requires [P2-01](P2-01-local-generation.md)** (GenerationService) |

## Objective

`generate_flashcards(note, count, format, output_note, dry_run)` in `GenerationTools`:
Q/A or cloze cards generated locally, with output in the Spaced Repetition plugin's format
(`#flashcards`, `Q::A`), CSV for Anki, or cloze.

## Acceptance criteria

- [ ] Model JSON validated with 1 retry; clean failure if it doesn't validate
  (`[error] [INTERNAL] model output could not be parsed`).
- [ ] All 3 formats render correctly (tests with a mocked service), including CSV
  escaping (commas/quotes/newlines).
- [ ] Output note with frontmatter `type: flashcards, source: "[[note]]"`; `dry_run`
  doesn't write.
- [ ] Without `KIOKU_GEN_MODEL`: `[error] [DEPENDENCY_UNAVAILABLE]` with instructions.
- [ ] Manual test: cards from a real note are readable by the Spaced Repetition plugin.
- [ ] `commands-reference.md` regenerated.

## Files

- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs`
- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (validated JSON output)
- Tests + docs
