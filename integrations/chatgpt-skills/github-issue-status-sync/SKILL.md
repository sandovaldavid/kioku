---
name: github-issue-status-sync
description: Reconciles GitHub issue status with merged and open PRs, current base-branch implementation, blockers, dependencies, and roadmap state. Use when the user asks which issues remain, whether completed issues should close, how issue status should be updated, or what work should come next.
license: MIT
compatibility: Requires authenticated GitHub read access; write access is required to update or close issues.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Synchronize Issue Status

## Procedure

1. Discover the repository's active base branch and current project direction.
2. Fetch relevant open and recently closed issues, comments, linked PRs, and recent merged/open PRs.
3. Verify implementation on the base branch; do not rely only on PR titles or issue checklists.
4. Classify each issue:
   - `completed`: acceptance criteria are present on the base branch;
   - `in progress`: an active PR or partial implementation exists;
   - `blocked`: a named dependency prevents progress;
   - `ready`: actionable and aligned with current direction;
   - `superseded`: replaced by a newer issue/decision;
   - `duplicate`;
   - `not planned`: contradicts current scope or was explicitly rejected;
   - `needs clarification`: acceptance criteria cannot be verified.
5. For completed issues:
   - close as `completed` only after verifying the merged implementation;
   - add a concise comment linking the implementing PR when useful.
6. For superseded, duplicate, or not-planned issues:
   - explain the evidence;
   - use the corresponding GitHub state reason;
   - link the replacement issue, PR, or decision.
7. For active issues:
   - update stale descriptions/checklists only when the required state is clear;
   - preserve historical discussion;
   - do not rewrite requirements silently.
8. Recommend an ordered next-work queue based on dependencies, architectural risk, and project value.
9. Publish a vault status note only when the reconciliation changes durable project direction or creates a handoff not represented by GitHub metadata.

## Output

Provide a table or compact sections with issue, classification, evidence, action taken, blocker/dependency, and recommended order.

Never close an issue merely because a related PR exists; verify the PR is merged into the correct base branch and satisfies acceptance criteria.
