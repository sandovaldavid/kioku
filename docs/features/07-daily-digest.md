# 07 — Daily digest

> Area: server · Task: [P2-03](../tasks/P2-03-daily-digest.md) · Impact ★★★ · Effort S

## Motivation

"What did I learn this week, what's overdue, what's still loose?" — today answering
this requires calling 4-5 separate tools. A single-tool digest is the most demo-able
feature for the student persona and builds a daily usage habit.

## Design

### `generate_digest(period = "day", target_folder = "", dry_run = false)`

In `WorkflowTools` (group `workflows`):

- `period`: `day` | `week`.
- Digest sections (all built from already-available data):
  1. **Activity** — notes created/modified during the period (`get_recent_activity` /
     `VaultIndexService`).
  2. **Tasks** — overdue and upcoming (`TaskService`).
  3. **New orphans** — notes from the period with no links (bounded
     `find_unlinked_notes`).
  4. **To review** — notes from the period with `draft`/`inbox` status.
- Writes the note to the `daily` folder from `.kioku/config.yml` (`folders.daily`,
  fallback `target_folder` or root) named `Digest {yyyy-MM-dd}.md` with frontmatter
  `type: log, tags: [digest]`. If it already exists, it's replaced (it's generated).
- `dry_run=true` returns the markdown without writing it.

### Optional enhancement with local generation

If `GenerationService` (spec 05) is available, adds a 3-4 line **Summary** section
generated locally from the period's titles/snippets. If not, the digest is purely
structural — the feature **doesn't depend** on 05.

## Affected files

- `src/Kioku.Mcp.Server/Tools/WorkflowTools.cs` (+1 tool)
- Reuse: `VaultIndexService`, `TaskService`, `GraphAnalysisTools` internals (extract to
  a service/helper if needed), `VaultConfigService.GetFolder("daily")`
- Tests: digest construction with a vault fixture (periods, empty sections)
- `docs/commands-reference.md` (regenerate)

## Risks

- Low. Clearly define the time cutoff (local midnight; `week` = last 7 days).
- Overwriting the existing digest is intentional — document it in the tool
  description.
