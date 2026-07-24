---
name: github-repo-docs-to-vault
description: Audits repository documentation and safely transfers decision rationale, historical context, private project knowledge, and session material into the Cortex-L7 Obsidian vault while preserving operational docs in the source repository. Use when deciding what belongs in a repo versus the vault or migrating existing docs.
license: MIT
compatibility: Requires authenticated GitHub write access to both the source repository and sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
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

1. Audit all Markdown, ADR, roadmap, notes, architecture, planning, and status files plus relevant issues/PRs.
2. Inspect code/configuration to distinguish current behavior from historical or proposed behavior.
3. Build a migration matrix with source path, classification, destination note type/path, source replacement, and rationale.
4. Prepare Cortex-L7 notes first:
   - decisions → ADR notes;
   - bug lessons → bug notes;
   - accepted plans → plan notes;
   - cross-repo/private knowledge → knowledge notes;
   - status/handoffs → session or daily notes.
5. Preserve source attribution and relevant issue/PR links, but do not copy secrets or raw conversations.
6. Open the vault PR.
7. Prepare the source-repository PR:
   - retain current operational facts;
   - replace moved rationale with a concise current-state statement;
   - add a stable reference when appropriate;
   - update indexes and links;
   - do not expose private vault details in a public repository.
8. State merge order:
   - vault PR first when the source PR removes unique content;
   - either order only when the source PR is additive and loses nothing.
9. Validate links, formatting, and completeness.
10. Do not mark migration complete until both PRs are reviewable and unique information is preserved.

## Public repository privacy

When the source repository is public and Cortex-L7 is private, do not add a direct private URL that reveals unwanted vault structure. Use a neutral note such as “Decision rationale is maintained in the private project knowledge base” unless the user explicitly wants a link.

## Output

Return the migration matrix, vault PR, source PR, merge order, validation evidence, unresolved ownership decisions, and risks.
