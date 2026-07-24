---
name: kioku-chat-orchestrator
description: Orchestrates repository work with persistent Obsidian project memory. Use when a user asks ChatGPT to resolve GitHub issues, review pull requests, maintain documentation, synchronize issue status, or migrate repository knowledge while reading and updating the Cortex-L7 vault through GitHub.
license: MIT
compatibility: Requires an Agent Skills-compatible client with authenticated GitHub read/write access to the source repository and sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.1.0"
  suite: kioku-chatgpt-skills
---

# Kioku Chat Orchestrator

Run the same lifecycle Kioku provides to CLI agents, using installed Agent Skills and the GitHub connector.

## Required inputs

Resolve these from the request, repositories, vault, or prior context:

- source repository in `owner/name` form;
- requested workflow and target issue or PR;
- source base branch;
- vault repository, default `sandovaldavid/Cortex-L7`;
- canonical Kioku project identifier resolved from the vault, not assumed from GitHub ownership.

Do not ask for data GitHub or the vault can determine. Do not assume branch names or vault paths.

## Workflow

1. **Verify access**
   - Confirm source-repository access.
   - Confirm Cortex-L7 read/write access before promising a vault PR.

2. **Resolve and load project memory**
   - Activate `kioku-project-context`.
   - Read `.kioku/config.yml` and resolve the existing semantic project identifier, for example `yukidoke/yukidoke-api` rather than `sandovaldavid/yukidoke-api`.
   - Read the project MOC and smallest relevant set of decisions, plans, bugs, knowledge, tickets, and recent sessions.
   - Read a parent `type: guide` note only for navigation or sibling context; never treat it as a project.
   - Treat source code/configuration as truth for current behavior and Cortex-L7 as truth for rationale, history, and handoffs.

3. **Select exactly one primary workflow**
   - issue implementation or bug fix: `github-issue-resolution`;
   - documentation audit/update: `github-documentation-maintenance`;
   - pull-request review or review follow-up: `github-pull-request-review`;
   - issue cleanup/status reconciliation: `github-issue-status-sync`;
   - repository-document classification or migration: `github-repo-docs-to-vault`.

4. **Execute the primary workflow**
   - Preserve its evidence and validation rules.
   - Do not silently broaden scope.
   - Do not claim commands, tests, builds, checks, or conflicts without direct evidence.
   - Use draft PRs when required validation remains unavailable.

5. **Extract durable memory**
   Determine whether the work produced a key decision, reusable bug lesson, accepted plan, cross-repository knowledge, status transition, or resumable handoff.

6. **Publish memory**
   - Activate `kioku-memory-publisher` for durable notes.
   - Activate `kioku-session-handoff` when work remains or another session must resume.
   - File cross-repository decisions in one concrete child project and link siblings in the body. Never create engineering notes loose at a group root.
   - Never include `.kioku/embeddings.bin`, `.obsidian/`, attachments, or unrelated Obsidian Git sync changes.

7. **Final reconciliation**
   Report source action and PR/issue state, resolved Kioku project, vault notes, vault PR, validation evidence, blockers, and exact next action.

## Guardrails

- Never treat an absent workflow as success.
- Never create a vault note from speculation.
- Never write directly to a protected base branch.
- Never store secrets or raw private conversation content.
- Never duplicate full repository documentation in the vault.
- Never mark the workflow complete while required source or vault work remains.
