# 11 — Flashcards / spaced repetition

> Area: server · Task: [P3-02](../tasks/P3-02-flashcards.md) · Impact ★★★ · Effort M
> **Depends on:** [05 — Local generation](05-local-generation.md)

## Motivation

★★★ feature for the student persona: turning notes into Q/A or cloze cards without
spending cloud agent tokens. The target formats already exist in the ecosystem: the
[Spaced Repetition](https://github.com/st3v3nmw/obsidian-spaced-repetition) plugin
(markdown with `#flashcards` and `Question::Answer`) and Anki (CSV import).

## Design

### `generate_flashcards(note, count = 10, format = "spaced-repetition", output_note = "", dry_run = false)`

In `GenerationTools` (group `generation`, created by spec 05):

- Reads the note's `PlainText` and asks `GenerationService` for `count` cards in
  strict JSON (`[{q, a}]` or `[{cloze}]`), with a fixed system prompt and parse
  validation (1 retry if the JSON doesn't validate).
- `format`:
  - `spaced-repetition` → markdown block with `#flashcards` + `Q::A` (or
    `¿...?::...`), written to `output_note` (default: `Flashcards/{note}.md`) or
    appended to the source note under `## Flashcards`.
  - `anki-csv` → CSV content (`front,back,tags`) returned in the response and/or
    written to a file in `folders.assets`.
  - `cloze` → the Spaced Repetition plugin's cloze variant.
- `dry_run`: returns the cards without writing them.
- Output note frontmatter: `type: flashcards, source: "[[note]]"`.

Requires `GenerationService.IsAvailable`; if not, `KiokuError.DependencyUnavailable`
with instructions (`KIOKU_GEN_MODEL`).

## Affected files

- `src/Kioku.Mcp.Server/Tools/GenerationTools.cs` (+1 tool)
- `src/Kioku.Mcp.Server/Services/GenerationService.cs` (validated JSON output helper)
- Tests: card parsing/validation (service mock), rendering of all 3 formats
- `docs/commands-reference.md` (regenerate)

## Risks

- Card quality with small models → prompt with few-shot examples; the user reviews
  before studying (position it as a draft).
- Malformed JSON from the model → validation + retry + a clean "couldn't generate"
  fallback.
- CSV escaping (commas/quotes in cards) — dedicated test.
