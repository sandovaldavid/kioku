---
layout: default
title: Engineering Workflows
sidebar: true
---

# Engineering workflows

Kioku keeps durable engineering context in ordinary Markdown project workspaces. The engineering model separates **what must be built** from **how it will be implemented**, while preserving existing decisions, bugs, knowledge, backlog, sessions, and optional local workflow notes.

The canonical flow is:

```text
request / issue
    ↓
engineering SPEC
    ↓
ADR(s), when independently durable decisions are warranted
    ↓
implementation PLAN
    ↓
SESSION / execution / review
    ↓
durable BUG / KNOWLEDGE / handoff outcomes
```

Kioku stores these artifacts and enforces vault-integrity rules. It does not execute a particular coding methodology or require an external workflow engine.

## Project workspace model

New project scaffolds create the durable core folders eagerly:

```text
decisions
bugs
specs
plans
knowledge
sessions
backlog
```

Two supported workflow folders remain optional and lazy:

```text
daily
tickets
```

An absent `daily/` or `tickets/` directory means that workflow has not been used yet; it is not a malformed project. Existing optional folders remain valid and require no migration.

See [Vault configuration](vault-config.md) for custom `engineering.subfolders` names, grouped projects, templates, and capability settings.

## Engineering specifications

A first-class engineering spec answers **what is being built and how it must behave**. Use `create_engineering_spec` for new specs rather than routing them through the deprecated generic project-document wrapper.

A spec contains an explicit durable structure for requirements and design context, including:

- objective and requirements;
- context and non-goals;
- architecture, components, and data flow;
- error handling and security/privacy considerations;
- compatibility and testing strategy;
- decisions, open questions, and related references.

The supported lifecycle is deliberately bounded:

```text
draft
approved
superseded
discarded
```

`get_project_context(types="spec")` and `get_project_context(types="specs")` are equivalent aliases. Approved specs are presented as current requirements, drafts as in-progress design, and superseded/discarded specs as historical evidence rather than current requirements.

## Spec to plan relationship

An implementation plan answers **how an approved/current design will be implemented in the current codebase**.

`create_implementation_plan` accepts an optional `spec` reference. When supplied, the plan records the relationship in frontmatter:

```yaml
spec: "[[SPEC-...]]"
```

The relation is metadata, not a plan body-template variable. Existing plan calls that omit `spec` remain valid.

Expected behavior by spec lifecycle:

| Spec status | New plan behavior |
|---|---|
| `approved` | Normal execution source |
| `draft` | Allowed with a provisional warning |
| `superseded` | Rejected for new execution work |
| `discarded` | Rejected for new execution work |

References must resolve to a spec in the same Kioku project. Path traversal, wrong-project references, malformed references, and unresolved heading-style references are rejected.

## Canonical spec identity

A canonical basename returned by `create_engineering_spec` is valid input for a later plan link, including generated names that contain characters meaningful to Markdown syntax.

Kioku resolves an exact basename inside the project's configured `specs` folder before applying optional reference-syntax interpretation. This preserves round trips for generated basenames containing literal `#`, dots, internal `..`, or a title that ends in `.md`, while still rejecting arbitrary paths and traversal.

Clients should prefer the canonical basename/path returned by Kioku rather than reconstructing a spec identity from a title.

## Durable revision and idempotency

Engineering writes use Kioku's normal guarded mutation boundary, including supported revision/hash preconditions, mutation IDs, and coordination fencing when supplied.

For spec creation and spec-linked plan creation, the returned revision identifies the **final durable file**. If an applicable Templater integration modifies the file after initial creation, Kioku re-reads the resulting file and returns the revision of those final bytes.

An idempotent retry of an already-applied mutation does not replay the external Templater side effect. Clients can therefore compare the returned revision with a later read to verify durable identity.

## Templates and Templater

Kioku installs the canonical engineering `spec` template through the same idempotent template-management path used by other engineering documents.

- existing user template overrides are not overwritten;
- existing user Templater folder mappings take precedence;
- `specs/` is a core project folder and can receive its configured template mapping;
- `daily/` and `tickets/` template mappings can exist before those optional folders are materialized.

Templater execution remains an optional Obsidian/plugin integration. A successful durable Kioku write must not become a failed write merely because an optional bridge is unavailable.

## External workflow engines

Kioku is the durable storage and data-integrity boundary, not the implementation workflow engine. An external agent or methodology can compose with Kioku without becoming a server dependency.

A safe generic integration follows this boundary:

```text
1. Load current project context through Kioku.
2. Inspect the current source repository and tracker context.
3. Produce/review the design using the external methodology.
4. Persist the approved SPEC through Kioku.
5. Produce/review the implementation plan.
6. Persist the PLAN through Kioku and link it to the SPEC.
7. Read the canonical plan and its revision before execution.
8. If a file-oriented workflow needs a local snapshot, derive it from that canonical plan.
9. Refuse to resume from a stale snapshot when the durable revision has changed.
10. Persist only durable outcomes back through Kioku.
```

Do not bypass Kioku by writing directly into the configured vault as an integration shortcut. Temporary task ledgers, raw subagent reports, review scratch, and similar orchestration state should stay local to the workflow engine unless they become durable knowledge that belongs in Kioku.

## Related references

- [MCP contract reference](commands-reference.md) — exact tool schemas and annotations.
- [Vault configuration](vault-config.md) — project folders, lifecycle behavior, templates, and aliases.
- [Focused-tool migration](focused-tool-migration.md) — preferred creation tools and deprecated wrappers.
- [Work sessions](work-sessions.md) — durable execution/session handoff.
- [Architecture](architecture.md) — internal application and storage boundaries.
- [Threat and privacy model](threat-and-privacy-model.md) — filesystem and external-service boundaries.
