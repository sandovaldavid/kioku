---
name: kioku-memory-publisher
description: Publishes durable engineering memory to the Cortex-L7 Obsidian vault through a GitHub branch and pull request. Use after meaningful repository work produces a decision, plan, bug lesson, reusable knowledge, status narrative, or project-memory update.
license: MIT
compatibility: Requires authenticated GitHub write access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.1.0"
  suite: kioku-chatgpt-skills
---

# Publish Kioku Project Memory

Read `references/vault-contract.md` and `references/knowledge-boundary.md` before publishing.

## Inputs

- source repository and relevant branch/ref;
- evidence from issues, PRs, code, configuration, or executed validation;
- verified or resolvable Kioku project identifier;
- note type and durable content;
- related issue/PR numbers when verified.

## Publication test

Publish only when the information is durable and changes a future agent's decisions or ability to resume. Do not publish chat transcripts, routine file lists, temporary hypotheses, duplicated setup content, unverified results, secrets, or sensitive payloads.

## Project routing

1. Resolve the project through `kioku-project-context`; do not map `owner/repository` directly to a vault path.
2. Respect the configured projects root and existing MOC `project:` value.
3. Treat parent `type: guide` notes as navigation only.
4. File cross-repository knowledge in one concrete child project and link the sibling projects in the note body.
5. When a project does not exist, use the repository leaf as the standalone fallback unless a verified semantic group exists.
6. Do not publish when project resolution remains ambiguous.

## Note routing

- decision with alternatives/trade-offs → `decisions/ADR-NNNN-<title>.md`, `type: decision`;
- diagnosed bug lesson → `bugs/BUG-YYYY-MM-DD-<title>.md`, `type: bug`;
- accepted execution plan → `plans/PLAN-YYYY-MM-DD-<title>.md`, `type: plan`;
- reusable technical/project knowledge → `knowledge/<descriptive-title>.md`;
- resumable state → delegate to `kioku-session-handoff`;
- ticket context that must outlive GitHub discussion → `tickets/` using neighboring naming;
- uncommitted idea → `backlog/<descriptive-title>.md`, `status: proposed`.

## Procedure

1. Verify vault access and discover its base branch.
2. Read `.kioku/config.yml`, the resolved project MOC, parent group guide when relevant, templates, and likely matching notes.
3. Deduplicate by topic, issue/PR, title, links, and semantic intent.
4. Choose update versus create:
   - update the canonical note for an existing topic;
   - create a new ADR only for a distinct decision;
   - mark a historical decision superseded and link the replacement rather than overwriting it.
5. Match neighboring frontmatter and the current vault contract. Use `project`, `project_link`, domain inheritance, canonical type, status, tags, CSS class, and filename conventions.
6. Put GitHub repository, issue, PR, branch, commands, and evidence in the body unless the canonical note already uses matching frontmatter fields.
7. Preserve Obsidian links, callouts, Dataview blocks, language, and user-authored sections.
8. Create `memory/<project-leaf>/<topic-slug>` from the current vault base branch.
9. Commit only coherent Markdown/configuration changes. Exclude `.kioku/embeddings.bin`, `.obsidian/`, attachments, and unrelated sync noise.
10. Open a vault PR documenting source evidence, durable value, notes changed, superseded or contradictory context, review guidance, and secret-safety confirmation.

## Decision exclusivity

Key decisions, alternatives, rationale, and consequences belong exclusively in Cortex-L7. Source repositories may state the current constraint and point to the private knowledge base when appropriate, but must not duplicate the full rationale.

## Failure behavior

If Cortex-L7 is unavailable or project resolution is ambiguous, do not pretend publication succeeded. Report the candidate project identifiers, intended note type/path, content summary, and exact blocker.
