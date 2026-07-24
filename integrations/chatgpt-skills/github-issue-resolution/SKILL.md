---
name: github-issue-resolution
description: Resolves a GitHub issue end to end with repository analysis, scoped implementation, tests, validation evidence, a correctly linked pull request, and an honest final report. Use when the user asks to fix, implement, continue, or complete a numbered issue.
license: MIT
compatibility: Requires authenticated GitHub access; executing repository commands additionally requires a real code execution environment.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Resolve a GitHub Issue

Read `references/issue-resolution-protocol.md` before implementation.

## Inputs

- repository `owner/name`;
- issue number;
- base branch, discovered when not explicit;
- any project-specific constraints.

## Procedure

1. Fetch the issue, all comments, related issues/PRs, and current labels/assignees.
2. Inspect repository instructions, current implementation, similar patterns, tests, scripts, and workflows on the base branch.
3. Define acceptance criteria, root cause, scope, risks, and validation plan.
4. Load relevant Cortex-L7 context through `kioku-project-context` when the orchestrator is active.
5. Create a fresh branch from the verified base branch.
6. Implement only the cohesive solution.
7. Add or update tests and documentation required by the behavior change.
8. Discover and execute real validation commands when an execution environment is available.
9. Review the diff and repository state.
10. Create a PR:
    - `Closes #N` only when complete;
    - `Refs #N` for partial work;
    - draft when required validation remains unavailable.
11. Verify PR metadata, conflicts, changed files, review state, and checks.
12. Publish durable decision, bug, plan, and handoff knowledge to Cortex-L7 through the orchestrator.

## ChatGPT connector limitation

The GitHub connector can inspect and mutate repository content, issues, and PRs, but it is not proof that local build/test commands ran. When no repository execution environment is connected:

- do not claim commands passed;
- document commands that must be run;
- mark them `No ejecutado`;
- prefer a draft PR;
- request or inspect user-supplied logs in a later turn.

## Required final report

Use the exact status vocabulary:

- `Passed`
- `Failed`
- `No configurado`
- `No ejecutado`

Include issue, analysis, implementation, validations, quality matrix, PR, vault-memory update, pending work, and risks.
