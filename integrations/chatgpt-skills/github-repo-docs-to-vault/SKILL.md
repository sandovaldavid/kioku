---
name: github-repo-docs-to-vault
description: Audits repository documentation and safely transfers decision rationale, historical context, private project knowledge, and session material into the Cortex-L7 Obsidian vault while preserving operational docs in the source repository. Use when deciding what belongs in a repo versus the vault or migrating existing docs.
license: MIT
compatibility: Requires authenticated GitHub write access to both the source repository and sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.1.0"
  suite: kioku-chatgpt-skills
---

# Move Repository Knowledge to Cortex-L7

Read `references/knowledge-boundary.md` and `references/vault-contract.md`.

## Classification

For every documentation file or coherent section, assign one action:

- `keep`: current contributor-facing or operational truth;
- `split`: contains both operational truth and durable rationale/history;
- `move`: belongs entirely in Cortex-L7;
- `archive`: useful historical repository material with no active operational role;
- `delete-duplicate`: fully duplicated and safely recoverable elsewhere;
- `needs-human-decision`: ownership or sensitivity is unclear.

## Procedure

1. Audit Markdown, ADR, roadmap, note, architecture, planning, and status files plus relevant issues and PRs.
2. Inspect code/configuration to distinguish current behavior from historical or proposed behavior.
3. Resolve the concrete Cortex-L7 project from its MOC and `.kioku/config.yml`; do not derive `owner/repository` paths.
4. Build a migration matrix with source path, classification, destination project/type/path, source replacement, and rationale.
5. Prepare Cortex-L7 notes first:
   - decisions → `decisions/ADR-NNNN-*`, `type: decision`;
   - bug lessons → `bugs/BUG-YYYY-MM-DD-*`;
   - accepted plans → `plans/PLAN-YYYY-MM-DD-*`;
   - cross-repository/private knowledge → `knowledge/`;
   - status/handoffs → project `sessions/` or `daily/`.
6. Never file engineering content at a parent `type: guide` group root. Choose one concrete child project and link affected siblings in the body.
7. Preserve source attribution in note-body references without inventing a new vault-wide frontmatter schema.
8. Open the vault PR, excluding `.kioku/embeddings.bin`, `.obsidian/`, attachments, and unrelated sync noise.
9. Prepare the source-repository PR:
   - retain current operational facts;
   - replace moved rationale with a concise current-state statement;
   - add a stable neutral reference when appropriate;
   - update indexes and links;
   - do not expose private vault structure in a public repository.
10. Merge vault first when the source PR removes unique content. Either order is acceptable only when the source PR is additive and loses nothing.
11. Validate links, formatting, classification completeness, and preservation of unique information.
12. Do not mark migration complete until both PRs are reviewable.

## Public repository privacy

When the source repository is public and Cortex-L7 is private, use wording such as “Decision rationale is maintained in the private project knowledge base” unless the user explicitly approves a private link.

## Output

Return resolved project identifier, migration matrix, vault PR, source PR, merge order, validation evidence, unresolved ownership decisions, and risks.
