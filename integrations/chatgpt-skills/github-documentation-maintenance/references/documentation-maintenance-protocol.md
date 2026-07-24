# Repository documentation maintenance protocol

This reference is derived from the user's `update-docs.prompts.md` and adds the required Cortex-L7 separation.

## Authority order

For current behavior:

1. code and configuration integrated in the base branch;
2. accepted current contracts;
3. merged PRs;
4. closed issues with clear resolution;
5. open issues and roadmap;
6. old documentation.

For rationale, alternatives, historical direction, and cross-project context, Cortex-L7 is authoritative.

## Audit sources

Inspect repository structure, source code, workspace/solution files, dependencies, scripts, workflows, Dockerfiles, infrastructure, environment configuration, tests, migrations, generators, hooks, i18n, documentation, relevant history, issues, PRs, roadmap, releases, and tags.

Classify claims as implemented, partial, planned, blocked, discarded, proposed, deprecated, or historical.

## AGENTS.md

Keep it operational:

- project purpose and current scope;
- architecture and dependency boundaries needed to edit safely;
- important directories;
- real development conventions;
- official generators and commands;
- Git workflow and issue protocol;
- validation policy and status vocabulary;
- restrictions and mandatory documentation updates;
- links to specialized docs.

Do not duplicate the entire README or docs tree. Do not store decision rationale there.

## README.md

Keep it introductory and verifiable:

- purpose and current status;
- implemented scope and clearly separated planned scope;
- technologies and architecture summary;
- prerequisites, installation, configuration, execution, quality commands, tests, build, Docker;
- concise structure and links;
- contribution and license information.

No false badges, invented links, unsupported commands, or unverified CI claims.

## docs/

Keep repository-owned documentation only:

- current architecture and module responsibilities;
- current APIs/contracts/data model;
- auth/roles required to operate the software;
- current i18n, theming, date/time, errors, logging, security, testing, configuration, setup, deployment, CI/CD, migrations, and troubleshooting;
- contributor-facing conventions.

Move or extract to Cortex-L7:

- key decisions and full rationale;
- alternatives and rejected approaches;
- personal or private notes;
- cross-repository strategy;
- historical status narratives;
- session handoffs;
- future planning not required for public roadmap tracking.

Use `github-repo-docs-to-vault` for classification and migration.

## Validation

Run configured Markdown formatting, markdownlint, link checks, docs generation/build, general linters, CI-equivalent checks, and affected tests when an execution environment exists. Manually verify relative links, paths, commands, branch names, issue references, examples, diagrams, and consistency.

Never claim an unexecuted validation passed.

## PR and report

Create a focused branch and PR. Document audited sources, updated/created/moved/archived documents, contradictions fixed, issues/PRs reviewed, information not confirmed, exact validation results, risks, and review instructions.

Do not close an issue unless the PR actually completes that issue.
