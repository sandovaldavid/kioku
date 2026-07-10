# Kioku vault rules

- Never call `delete_note` with `permanent=true`, `revert_all_uncommitted`, `merge_tags`, or
  `rename_tag_globally` without first calling the same tool with `dry_run=true` and showing the
  user the result in this turn.
- Never call `revert_all_uncommitted` or a `permanent=true` delete without an explicit,
  unambiguous instruction from the user in the current conversation — do not infer consent from
  earlier turns or from a general "clean up the vault" request.
- Prefer `search_notes_hybrid` over guessing between keyword and semantic search unless the
  user's intent is clearly one or the other.
- Do not create notes outside the vault's configured folders (`.kioku/config.yml`) without a
  clear reason; check `get_vault_stats` if unsure of the vault's layout.
- Before a bulk edit across many notes, check `get_git_status` and suggest committing first so
  the change can be reviewed or reverted.
