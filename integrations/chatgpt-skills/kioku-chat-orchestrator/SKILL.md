---
name: kioku-chat-orchestrator
description: Orchestrates repository work with persistent Obsidian project memory. Use when a user asks ChatGPT to resolve GitHub issues, review pull requests, maintain documentation, synchronize issue status, or migrate repository knowledge while reading and updating the Cortex-L7 vault through GitHub.
license: MIT
compatibility: Requires an Agent Skills-compatible client with authenticated GitHub read/write access to the source repository and sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Kioku Chat Orchestrator

Run the same high-level lifecycle that Kioku provides to CLI agents, but use installed Agent Skills and the GitHub connector.

## Required inputs

Resolve these from the request, repository metadata, or prior context:

- source repository in `owner/name` form;
- requested workflow and target issue or PR, when applicable;
- source base branch;
- vault repository, defaulting to `sandovaldavid/Cortex-L7`;
- canonical project identifier, defaulting to the source repository full name.

Do not ask for information that GitHub can determine. Do not assume branch names.

## Workflow

1. **Verify access**
   - Confirm read access to the source repository.
   - Confirm read/write access to the vault repository before promising a vault PR.
   - If the vault is inaccessible, continue source analysis when safe, but report memory publication as blocked.

2. **Load durable context**
   - Activate `kioku-project-context`.
   - Read the project MOC and the smallest relevant set of decisions, plans, bugs, knowledge notes, tickets, and recent sessions.
   - Treat repository code/configuration as truth for current behavior and Cortex-L7 as truth for rationale, history, and handoffs.

3. **Select exactly one primary workflow**
   - issue implementation or bug fix: `github-issue-resolution`;
   - documentation audit/update: `github-documentation-maintenance`;
   - pull-request review or review follow-up: `github-pull-request-review`;
   - issue cleanup/status reconciliation: `github-issue-status-sync`;
   - repository-document classification or migration: `github-repo-docs-to-vault`.

4. **Execute the primary workflow**
   - Preserve its evidence and validation rules.
   - Do not silently broaden scope.
   - Do not claim local commands, tests, builds, or checks ran unless an execution environment actually ran them.
   - Use draft PRs when required validation remains unavailable.

5. **Extract durable memory**
   Determine whether the work produced any of:
   - a key decision or changed direction;
   - a reusable root-cause/fix lesson;
   - an accepted implementation plan;
   - cross-repository knowledge;
   - a status transition not fully represented by GitHub metadata;
   - a resumable handoff.

6. **Publish memory**
   - Activate `kioku-memory-publisher` for durable notes.
   - Activate `kioku-session-handoff` when work remains, validation is pending, or a future session must resume from a known state.
   - Keep decision rationale out of source-repository documentation.

7. **Final reconciliation**
   Report:
   - source repository action and PR/issue state;
   - vault notes created or updated;
   - vault PR state;
   - validation evidence;
   - blockers and exact next action.

## Guardrails

- Never treat an absent GitHub Actions run as success.
- Never create a vault note from speculation.
- Never write directly to a protected base branch.
- Never store secrets or raw private conversation content.
- Never duplicate full repository documentation in the vault.
- Never mark the workflow complete when the source PR or vault PR still has known required work.
