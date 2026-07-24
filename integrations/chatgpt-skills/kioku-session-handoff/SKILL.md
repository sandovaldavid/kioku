---
name: kioku-session-handoff
description: Creates a resumable Kioku-style engineering session handoff in Cortex-L7. Use when a GitHub task ends, pauses, is blocked, awaits validation or review, or needs the next ChatGPT or CLI-agent session to continue from an exact state.
license: MIT
compatibility: Requires authenticated GitHub write access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Record Session Handoff

A handoff is operational memory, not a narrative transcript.

## Required content

Capture only verified facts:

- session goal;
- source repository and branch;
- issue and PR references;
- work completed;
- files or modules materially changed;
- validation commands actually executed and exact results;
- checks not executed and why;
- current blockers;
- decisions made, linking separate ADR notes;
- next ordered actions;
- resume command or starting point when useful.

## Procedure

1. Load the most recent session note for the same project and objective.
2. Update it when continuing the same active thread; otherwise create a new dated session note.
3. Use `sessions/YYYY-MM-DD-<topic>.md`.
4. Set status:
   - `active` when work is continuing now;
   - `blocked` when an external dependency prevents progress;
   - `waiting` when awaiting logs, review, merge, approval, or access;
   - `completed` only when all required work and available validation are complete.
5. Link relevant ADR, bug, plan, issue, and PR notes.
6. Publish through `kioku-memory-publisher` and a vault PR.

## Template

```markdown
---
type: session
status: waiting
project: owner/repository
source_repo: owner/repository
source_ref: branch-name
source_issue: 123
source_pr: 456
date: YYYY-MM-DD
updated: YYYY-MM-DD
tags: [kioku, engineering, session]
---

# Session: concise objective

## Goal

## Completed

## Current state

## Validation evidence

## Blockers

## Decisions

## Next actions

1. ...

## Resume from

- Branch:
- First command or inspection:
- Expected evidence:
```

Never mark a session `completed` because a workflow was skipped or unavailable.
