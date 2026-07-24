# Repository and vault knowledge boundary

## Source repository is authoritative for

- source code and generated artifacts that are intentionally tracked;
- executable configuration and environment-variable contracts;
- current public behavior, APIs, schemas, data models, and compatibility guarantees;
- installation, development, testing, build, deployment, and troubleshooting instructions;
- current architecture needed to modify or operate the repository safely;
- contribution rules and agent operating instructions;
- current security, accessibility, i18n, observability, and release procedures.

## Cortex-L7 is authoritative for

- key decisions and their rationale;
- alternatives considered, rejected options, and trade-offs;
- cross-repository and portfolio-level context;
- project history and changes in direction;
- private or personal notes;
- accepted future direction and planning context;
- work-session handoffs and resumable state;
- bugs as learned knowledge, including root cause and remediation lessons;
- status narratives that should survive individual issues and PRs.

## Duplication rule

A current fact may appear in both places only when each copy serves a different operational purpose. The repository copy explains what contributors must know now. The vault copy explains why the state exists, how it evolved, or how it connects to other work.

Do not copy long decision rationale into `README.md`, `AGENTS.md`, or `docs/`. A short current-state statement and a stable reference are sufficient.

## Migration safety

Never delete or substantially reduce a repository document until:

1. the corresponding vault note has been prepared;
2. a vault pull request exists;
3. the repository replacement or summary is ready;
4. no setup, contract, or operational information would be lost;
5. both PRs describe the dependency and safe merge order.
