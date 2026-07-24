# Kioku workflow for a ChatGPT Project

Use these instructions when Personal Skills are unavailable.

For every substantial GitHub repository task:

1. Identify the source repository, target issue/PR, and verified base branch.
2. Access `sandovaldavid/Cortex-L7` through the GitHub connector and read `.kioku/config.yml`.
3. Resolve the existing semantic Kioku project from its MOC. Cortex-L7 currently stores projects under `20-execution` and uses identifiers such as `yukidoke/yukidoke-api`, `atena/api.core`, or standalone `fluentreads`; never assume `Projects/<owner>/<repository>`.
4. Treat parent notes with `type: guide` as navigation only. Never store decisions, plans, bugs, knowledge, sessions, tickets, or backlog items loose at a group root.
5. Load the project MOC, relevant decisions/plans/bugs/knowledge/tickets, and latest project session.
6. Execute the matching workflow: issue resolution, documentation maintenance, PR review, issue status synchronization, or repository-doc migration.
7. Treat source code/configuration as truth for current behavior and Cortex-L7 as the exclusive source for key decisions, alternatives, rationale, historical direction, private cross-repository knowledge, and handoffs.
8. Never claim tests, builds, checks, or conflicts without direct evidence.
9. Publish durable memory using the vault's existing conventions: `type: decision`, `BUG-*`, `PLAN-*`, project sessions named `YYYY-MM-DD-HHmm-chatgpt.md`, and status `done` rather than `completed`.
10. Create vault changes on a dedicated branch and PR. Never include `.kioku/embeddings.bin`, `.obsidian/`, attachments, or unrelated Obsidian Git sync changes.
11. End with source state, resolved Kioku project, validation evidence, vault notes, vault PR, blockers, and exact next action.

When the vault is inaccessible or project resolution is ambiguous, continue safe source analysis but report persistent-memory publication as blocked and list candidate projects or intended note types.
