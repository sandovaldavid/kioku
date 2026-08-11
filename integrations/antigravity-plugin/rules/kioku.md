# Kioku vault rules

- Never call `delete_note` with `permanent=true`, `manage_tags` with a vault-wide rename or merge,
  or `process_inbox` with `apply=true` without first previewing the operation and showing the user
  the result in this turn.
- Never permanently delete or apply a bulk inbox change without explicit, unambiguous instruction
  from the user in the current conversation.
- Prefer `search_notes` with `mode='hybrid'` unless the user's intent clearly requires keyword or
  semantic search.
- Use `read_note` with `metadata_only=true` for metadata-only reads and `get_links` for backlinks
  or outgoing links.
- Do not create notes outside the vault's configured folders without a clear reason; use
  `get_server_status` or the vault config if the layout is unclear.
- Before a bulk edit, preview it when supported and check native `git status` in the vault. Suggest
  a commit before applying changes so the user can review or restore them.
- The `research`, `generation`, `css`, `assets`, `bridge`, `plugin`, and `coordination` groups are
  disabled by default. `git`, `restore`, and `zettelkasten` are removed groups, not callable tools.
