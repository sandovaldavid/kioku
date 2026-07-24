---
name: kioku-memory-publisher
description: Publishes durable engineering memory to the Cortex-L7 Obsidian vault through a GitHub branch and pull request. Use after meaningful repository work produces a decision, plan, bug lesson, reusable knowledge, status narrative, or project-memory update.
license: MIT
compatibility: Requires authenticated GitHub write access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Publish Kioku Project Memory

Read `references/vault-contract.md` and `references/knowledge-boundary.md` before publishing.

## Inputs

- source repository and relevant branch/ref;
- evidence from issues, PRs, code, configuration, or executed validation;
- project identifier;
- note type and durable content;
- related issue/PR numbers when verified.

## Publication test

Publish only when the information is durable and changes a future agent's decisions or ability to resume. Do not publish:

- chat transcripts;
- routine file lists;
- temporary hypotheses;
- duplicated README/setup content;
- unverified test results;
- secrets or sensitive payloads.

## Note routing

- decision with alternatives/trade-offs → `decisions/ADR-NNNN-<slug>.md`;
- diagnosed bug lesson → `bugs/<date>-<slug>.md`;
- accepted execution plan → `plans/<date>-<slug>.md`;
- reusable technical/project knowledge → `knowledge/<slug>.md`;
- resumable state → delegate to `kioku-session-handoff`;
- ticket context that must outlive GitHub discussion → `tickets/<ticket>-<slug>.md`;
- uncommitted idea → `backlog/<date>-<slug>.md`.

## Procedure

1. Verify vault repository access and discover its base branch.
2. Read `.kioku/config.yml`, the project MOC, and likely matching notes.
3. Deduplicate by topic, source issue/PR, title, and semantic intent.
4. Choose update versus create:
   - update the canonical note for an existing topic;
   - create a new ADR only for a distinct decision;
   - never overwrite historical decisions—mark them superseded and link the replacement.
5. Build evidence-based frontmatter. Omit unknown fields.
6. Preserve Obsidian links and add related-note links where supported by evidence.
7. Create a branch named `memory/<project-leaf>/<topic-slug>`.
8. Commit the coherent note changes.
9. Open a PR to the discovered vault base branch.
10. In the PR body include:
    - source repository and issue/PR;
    - why the memory is durable;
    - notes created/updated;
    - contradictions or superseded notes;
    - review guidance;
    - confirmation that no secrets were added.

## Decision exclusivity

Key decisions, alternatives, rationale, and consequences belong exclusively in Cortex-L7. Source repositories may state the current constraint and link or refer to the decision, but must not duplicate the full rationale.

## Failure behavior

If Cortex-L7 is not authorized, do not pretend publication succeeded. Produce the intended note paths and content summary, mark the vault update blocked, and state that GitHub App access must be granted.
