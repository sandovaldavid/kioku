# Focused MCP tool migration

**Status:** focused tools are **Implemented** on `develop`; `create_note` and `create_project_doc` are **Deprecated** compatibility wrappers.

Kioku exposes focused creation tools for common note and engineering intents. New integrations, prompts, and agent instructions should use the focused surface.

## Engineering tools

| Deprecated call | Focused replacement |
|---|---|
| `create_project_doc(doc_type="adr", ...)` | `record_adr(...)` |
| `create_project_doc(doc_type="bug", ...)` | `record_bug(...)` |
| `create_project_doc(doc_type="plan", ...)` | `create_implementation_plan(...)` |
| `create_project_doc(doc_type="knowledge", ...)` | `save_project_knowledge(...)` |
| `create_project_doc(doc_type="backlog", ...)` | `add_backlog_item(...)` |

### First-class engineering specs

`spec` is a first-class project-document type rather than another `create_project_doc` compatibility mode. Use:

```text
create_engineering_spec(...)
```

for durable design/requirements documents.

When an implementation plan is based on a spec, pass the canonical same-project spec reference through the additive `spec` parameter:

```text
create_implementation_plan(..., spec="SPEC-...")
```

Existing `create_implementation_plan` callers that omit `spec` remain valid. The resulting relation is stored as frontmatter metadata and does not add `spec` to the plan body-template variable contract.

See [Engineering workflows](engineering-workflows.md) for spec lifecycle, project scaffold semantics, canonical identity, revision behavior, and external workflow composition.

## Note creation tools

| Deprecated call | Focused replacement |
|---|---|
| `create_note(kind="note", ...)` | `create_regular_note(...)` |
| `create_note(kind="zettel", ...)` | `create_zettel(...)` |
| `create_note(kind="literature", ...)` | `create_literature_note(...)` |
| `create_note(kind="moc", ...)` | `create_moc(...)` |
| `create_note(kind="folder-readme", ...)` | `create_folder_readme(...)` |

## Compatibility policy

- Deprecated wrappers remain callable for at least one minor server release after the focused tools are published in a tagged release.
- Focused tools delegate to the same application behavior, so filesystem safety, capability gating, validation, and typed result envelopes remain consistent.
- New prompts, skills, and integrations must use focused names.
- `create_project_doc` does not provide a compatibility alias for first-class specs; new spec workflows use `create_engineering_spec` directly.
- Removing a deprecated wrapper requires a separate compatibility change, generated contract update, migration note, and release entry.

The exact public schemas are generated in [commands-reference.md](commands-reference.md). Do not infer removal timing from an issue or an unreleased branch.

## Schema design

Focused tools should expose parameters that belong to one coherent intent rather than conditional fields from unrelated note/document types.

Most focused creation tools remain deliberately small. `create_engineering_spec` is an intentional structured-document exception: its optional parameters correspond directly to canonical spec sections such as architecture, data flow, error handling, security/privacy, compatibility, testing strategy, decisions, and open questions. They are all part of the single engineering-spec intent rather than fields borrowed from another document type.

The generated [MCP contract reference](commands-reference.md) is authoritative for the current parameter set. Contract tests verify the public surface and prevent accidental schema drift.
