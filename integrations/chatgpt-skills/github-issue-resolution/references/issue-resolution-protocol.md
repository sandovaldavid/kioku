# Issue resolution protocol

This reference is derived from the user's `issue-fix.prompt.md`. It preserves its evidence, scope, validation, and PR requirements while allowing execution through ChatGPT and the GitHub connector.

## 1. Understand before editing

Read the complete issue, comments, acceptance criteria, labels, dependencies, related issues/PRs, and roadmap. Inspect the current base branch, `AGENTS.md`, `README.md`, `CONTRIBUTING.md`, `docs/`, ADR references, framework rules, naming, structure, lint/format configuration, CI workflows, templates, and similar implementations.

Identify root cause or real need, exact scope, verifiable acceptance criteria, affected modules, regression risks, required tests, and dependencies.

## 2. Keep the change cohesive

Use one PR when the solution is cohesive and reviewable. Split only for a clear technical reason such as a required precursor, migration sequence, independent refactor, deployment incompatibility, or unsafe review size. Explain merge order and use `Refs` until all required slices exist.

Do not add unrelated refactors, dependencies, or improvements.

## 3. Branch correctly

Refresh repository state, verify the base branch, create a descriptive branch from its latest commit, and ensure no unrelated changes are included.

## 4. Implement within established architecture

Respect separation of responsibilities, framework conventions, generators, file structure, error handling, contracts, security, accessibility, i18n, theming, observability, compatibility, testing, and documentation rules.

Do not duplicate logic, add unjustified dependencies, include secrets, weaken controls, or collapse structured components into one file.

## 5. Add meaningful tests

Where technically possible, include a regression test that fails before the fix, main-path coverage, boundary/error coverage, and legitimate updates to changed contracts. Never disable, omit, or weaken tests only to make a suite pass.

## 6. Discover real validation commands

Derive commands from project files and CI. Applicable controls may include reproducible install, formatting, lint, type checking, build, unit/integration/component/E2E/accessibility/contract/architecture/security tests, migrations, packaging, and docs generation.

Run focused checks first, then the required full suite. If no execution environment exists, mark every unexecuted command `No ejecutado`; never infer success.

## 7. Review the final diff

Check scope, logic, edge cases, accidental files, secrets, security regressions, formatting, line endings, consistency, commit focus, maintainability, names, duplication, error handling, typing, accessibility, i18n, documentation, tests, and cross-module impact.

## 8. Create and verify the PR

Use a clear title and `Closes #N` only when the PR fully resolves the issue. Otherwise use `Refs #N`. The PR body must document problem, root cause, solution, decisions, main files, tests, exact validation commands/results, risks, manual verification, and dependencies.

After creation verify source/base branches, commits, changed files, description, issue link, conflicts, and checks.

When GitHub Actions is unavailable or quota-limited, run local equivalents when an execution environment exists and state which remote workflows did not run and why. Absence is not success.

## 9. Evidence policy

Never claim a test, build, conflict check, or workflow passed unless directly observed. For each validation report exact command, result, test count when available, warnings/errors, and environmental limitations.

Use a draft PR when required validation is unavailable and a draft still provides value.

## 10. Final report

Report issue, objective, acceptance criteria, analysis, consulted rules, technical decisions, implementation, affected modules, tests, each validation and evidence, quality matrix (`Passed`, `Failed`, `No configurado`, `No ejecutado`), PR details, conflicts/checks, pending work, risks, and manual validation.
