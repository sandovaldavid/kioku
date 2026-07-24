---
name: kioku-session-handoff
description: Creates a resumable Kioku-style engineering session handoff in Cortex-L7. Use when a GitHub task ends, pauses, is blocked, awaits validation or review, or needs the next ChatGPT or CLI-agent session to continue from an exact state.
license: MIT
compatibility: Requires authenticated GitHub write access to sandovaldavid/Cortex-L7.
metadata:
  author: sandovaldavid
  version: "1.1.0"
  suite: kioku-chatgpt-skills
---

# Record Session Handoff

A handoff is operational memory, not a narrative transcript.

## Required content

Capture verified facts only:

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

1. Resolve the concrete Kioku project; never file a project handoff in a parent `type: guide` group.
2. Load the latest session note for the same project and objective.
3. Continue an active note only when it is clearly the same thread; otherwise create a new note.
4. Store project work in `<project>/sessions/`. Use the configured global `sessions` folder only when no project exists.
5. Name a new ChatGPT session `sessions/YYYY-MM-DD-HHmm-chatgpt.md` using the user's local time when known.
6. Use status:
   - `active` while work is continuing;
   - `blocked` when an external dependency prevents progress;
   - `waiting` when awaiting logs, review, merge, approval, or access;
   - `done` only when all required work and available validation are complete.
7. Use `agent: chatgpt`, `type: session`, tags `session` and `work-log`, CSS class `kioku-session`, canonical `project`, `project_link`, inherited `domain`, and the session date.
8. Link relevant ADR, bug, plan, issue, PR, and knowledge notes.
9. Publish through `kioku-memory-publisher` on a vault branch and PR.

## Template

```markdown
---
tags:
  - session
  - work-log
cssclasses:
  - kioku-session
type: session
status: waiting
domain: tech
date: YYYY-MM-DD
project: group/project
project_link: "[[project-leaf]]"
agent: chatgpt
---

# Work Session — YYYY-MM-DD HH:mm (chatgpt)

> [!info] Session
> Project: [[project-leaf]] · Started: YYYY-MM-DD HH:mm · Agent: chatgpt

**Goal:** concise objective

## Summary

## Log

## Modified during this session

## Validation evidence

## Blockers

## Decisions

## Next actions

1. ...

## Resume from

- Source branch:
- First command or inspection:
- Expected evidence:

---

## Session ended — HH:mm local

**Outcome:** verified status and remaining work.
```

Never use `completed` when the vault convention is `done`, and never mark a session `done` because a workflow was skipped or unavailable.
