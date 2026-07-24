---
name: github-documentation-maintenance
description: Audits and updates a repository's AGENTS.md, README.md, and docs against current code, configuration, issues, and PRs while moving decision rationale and historical project memory to Cortex-L7. Use for full documentation reviews, documentation refreshes, or stale-doc cleanup.
license: MIT
compatibility: Requires authenticated GitHub access to the source repository; vault migration additionally requires access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Maintain Repository Documentation

Read `references/documentation-maintenance-protocol.md`.

## Procedure

1. Discover and verify the base branch.
2. Audit code, configuration, tests, scripts, workflows, issues, PRs, and all existing docs.
3. Classify every material claim by current implementation status.
4. Apply the repository/vault boundary:
   - keep current contributor and operational truth in the repository;
   - route decisions, alternatives, historical direction, private context, and handoffs through `github-repo-docs-to-vault`.
5. Update `AGENTS.md` as an operational agent manual.
6. Update `README.md` as the concise project entry point.
7. Improve `docs/` without forcing a new structure when the current structure is coherent.
8. Add or update `docs/README.md` when navigation warrants it.
9. Remove or archive duplicates only after confirming no unique current information is lost.
10. Validate formatting, links, docs generation, commands, and affected checks when execution is available.
11. Review the final diff and create a focused PR.
12. Publish durable findings to Cortex-L7 through the orchestrator.

## Non-negotiable rules

- Never document an open issue as implemented functionality.
- Never invent commands, versions, dependencies, endpoints, roles, environment variables, coverage, or test results.
- Never copy full decision rationale back into repository docs.
- Never delete a decision/history document before the vault migration is prepared and recoverable.
- Mark validation as `Passed`, `Failed`, `No configurado`, or `No ejecutado`.

## Final report

Include audited sources, contradictions, stale/duplicate/missing information, files changed, vault migrations, exact validation results, source PR, vault PR, unconfirmed facts, pending decisions, and risks.
