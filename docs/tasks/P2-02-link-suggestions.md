# P2-02 — Link suggestions

| Field | Value |
|---|---|
| Priority | P2 |
| Branch | `feat/link-suggestions` |
| Commit | `feat(server): add suggest_links and apply_link_suggestions tools` |
| Size | M |
| Spec | [features/06-link-suggestions.md](../features/06-link-suggestions.md) |
| Dependencies | No hard dependency (uses existing embeddings); improves P2-04 |

## Objective

`suggest_links(note?, max_suggestions, min_similarity)` (unlinked semantic candidates;
vault mode prioritizes orphans/islands) and `apply_link_suggestions(note, targets, section)`
(`## Related` section, idempotent, with `dry_run`) in `GraphAnalysisTools`.

## Acceptance criteria

- [ ] `suggest_links` never proposes pairs that are already linked (in either direction) nor
  a note linked to itself; output includes score, snippet and reason.
- [ ] Without Ollama: per-note mode returns `[error] [DEPENDENCY_UNAVAILABLE]`; vault mode
  degrades to structural analysis (orphans/islands) with a warning.
- [ ] `apply_link_suggestions` is idempotent (a second run doesn't duplicate) and respects
  `dry_run`.
- [ ] Reuse verified: don't duplicate `link_related_notes`'s insertion logic
  (extract a shared helper if needed).
- [ ] Tests with `VaultFixture` (filtering, idempotency, degradation) green.
- [ ] `commands-reference.md` regenerated + README tables updated.

## Files

- `src/Kioku.Mcp.Server/Tools/GraphAnalysisTools.cs`
- `src/Kioku.Mcp.Server/Tools/ZettelkastenTools.cs` (if a helper is extracted)
- Tests + docs
