---
name: github-pull-request-review
description: Performs an evidence-based GitHub pull-request review covering scope, architecture, correctness, tests, security, documentation, review threads, conflicts, and checks. Use when the user asks to review a PR, assess merge readiness, inspect changes, or address review feedback.
license: MIT
compatibility: Requires authenticated GitHub read access; write access is required to submit reviews, comments, or fixes.
metadata:
  author: sandovaldavid
  version: "1.0.0"
  suite: kioku-chatgpt-skills
---

# Review a Pull Request

## Inputs

- repository and PR number;
- optional review objective or project constraints.

## Procedure

1. Fetch PR metadata, base/head refs, body, commits, changed filenames, full patch, issue links, comments, reviews, and unresolved threads.
2. Read repository instructions and relevant Cortex-L7 decisions before judging architecture or scope.
3. Verify:
   - stated problem and issue relationship;
   - scope cohesion and unrelated changes;
   - architecture and dependency direction;
   - correctness, edge cases, error handling, concurrency, data integrity;
   - security, privacy, secrets, authorization;
   - accessibility, i18n, theming, observability, compatibility;
   - tests and regression coverage;
   - documentation and migration impact;
   - generated files and configuration;
   - conflicts and current checks.
4. Separate findings:
   - `blocking`;
   - `important`;
   - `suggestion`;
   - `question`;
   - `verified good`.
5. Anchor findings to exact files and lines whenever possible.
6. Do not infer a passing build or test suite from code inspection.
7. When asked to fix findings, use a dedicated branch or the PR head only when authorized, preserve scope, and rerun available validation.
8. Record a durable ADR or bug lesson only when the review establishes a reusable project decision or root-cause insight.

## Merge recommendation

Return exactly one:

- `Ready to merge`
- `Ready after non-blocking follow-up`
- `Changes required`
- `Not assessable with current evidence`

State the evidence and limitations behind the recommendation.

## Review output

```text
PR:
Intent:
Issue relationship:
Blocking findings:
Important findings:
Suggestions:
Validation/check state:
Conflicts:
Decision impact:
Merge recommendation:
Required next action:
```
