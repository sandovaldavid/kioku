# Focused MCP tool migration

Kioku now exposes focused creation tools for common note and engineering intents. The existing `create_note` and `create_project_doc` tools remain available during the compatibility window, but new integrations should prefer the focused surface.

## Engineering tools

| Legacy call | Focused replacement |
|---|---|
| `create_project_doc(doc_type="adr", ...)` | `record_adr(...)` |
| `create_project_doc(doc_type="bug", ...)` | `record_bug(...)` |
| `create_project_doc(doc_type="plan", ...)` | `create_implementation_plan(...)` |
| `create_project_doc(doc_type="knowledge", ...)` | `save_project_knowledge(...)` |
| `create_project_doc(doc_type="backlog", ...)` | `add_backlog_item(...)` |

## Note creation tools

| Legacy call | Focused replacement |
|---|---|
| `create_note(kind="note", ...)` | `create_regular_note(...)` |
| `create_note(kind="zettel", ...)` | `create_zettel(...)` |
| `create_note(kind="literature", ...)` | `create_literature_note(...)` |
| `create_note(kind="moc", ...)` | `create_moc(...)` |
| `create_note(kind="folder-readme", ...)` | `create_folder_readme(...)` |

## Compatibility policy

- Legacy tools remain callable for at least one minor release after focused tools are published.
- Focused tools delegate to the same application behavior, so filesystem safety, capability gating, validation, and typed result envelopes remain consistent.
- New prompts, skills, and integrations should use focused names immediately.
- Removal of legacy tools requires a separate compatibility change and release note.

## Schema budget

Focused tools expose at most eight parameters and contain no conditional parameters belonging to another intent. Contract tests enforce this upper bound and verify that representative unrelated parameters cannot leak between tool schemas.

This reduces schema token cost and makes tool selection more deterministic while preserving the existing capability set.
