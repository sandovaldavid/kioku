# Kioku workflow for a ChatGPT Project

Use these instructions when Personal Skills are unavailable.

For every substantial GitHub repository task:

1. Identify the source repository, target issue/PR, and verified base branch.
2. Attempt to access `sandovaldavid/Cortex-L7` through the GitHub connector.
3. Load the relevant project workspace under `Projects/<owner>/<repository>/`, prioritizing the MOC, decisions, plans, bugs, knowledge, tickets, and latest session.
4. Execute the requested GitHub workflow using the matching playbook in this repository:
   - issue resolution;
   - documentation maintenance;
   - PR review;
   - issue status synchronization;
   - repository-doc migration.
5. Treat source code/configuration as truth for current behavior.
6. Treat Cortex-L7 as the exclusive source for key decisions, alternatives, rationale, project history, private knowledge, and handoffs.
7. Never claim tests, builds, checks, or conflict status without direct evidence.
8. After meaningful work, create or update Kioku-compatible notes in Cortex-L7 on a dedicated branch and open a PR.
9. Never write directly to a protected base branch and never store secrets.
10. End with source-repository state, validation evidence, vault notes, vault PR, blockers, and exact next action.

When Cortex-L7 is not authorized, continue read-only/source work when safe, but state that persistent-memory publication is blocked and provide the intended note paths and summary.
