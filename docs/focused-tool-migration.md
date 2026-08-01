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
- Removing a deprecated wrapper requires a separate compatibility change, generated contract update, migration note, and release entry.

The exact public schemas are generated in [commands-reference.md](commands-reference.md). Do not infer removal timing from an issue or an unreleased branch.

## Schema budget

Focused tools expose at most eight parameters and contain no conditional parameters belonging to another intent. Contract tests enforce this upper bound and verify that representative unrelated parameters cannot leak between tool schemas.

This reduces schema token cost and makes tool selection more deterministic while preserving the implemented capability set.
