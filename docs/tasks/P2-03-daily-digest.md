# P2-03 — Daily digest

| Field | Value |
|---|---|
| Priority | P2 |
| Branch | `feat/daily-digest` |
| Commit | `feat(server): add generate_digest tool for daily and weekly reviews` |
| Size | S |
| Spec | [features/07-daily-digest.md](../features/07-daily-digest.md) |
| Dependencies | No hard dependency; if P2-01 is merged, adds a local summary section |

## Objective

`generate_digest(period = day|week, target_folder, dry_run)` in `WorkflowTools`: a note
generated with activity for the period, overdue/due-soon tasks, new orphans and notes in
`draft`/`inbox`, written to `folders.daily` with frontmatter `type: log, tags: [digest]`.

## Acceptance criteria

- [ ] Correct digest with fixture: day/week periods, empty sections are omitted with a
  "nothing new" heading, documented time cutoff (local midnight / 7 days).
- [ ] Re-running on the same day replaces the note (behavior documented in the tool's
  description).
- [ ] `dry_run=true` returns the markdown without writing.
- [ ] With `GenerationService` available, adds a local **Summary** section; without it, the
  digest is still generated (no failure).
- [ ] `commands-reference.md` regenerated + README tables updated.

## Files

- `src/Kioku.Mcp.Server/Tools/WorkflowTools.cs`
- Reuse: `VaultIndexService`, `TaskService`, `VaultConfigService`
- Tests with `VaultFixture` + docs
