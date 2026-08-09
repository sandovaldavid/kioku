# Migrate from Kioku 2.3.0 to 3.0.0

Kioku 3.0.0 is published. This guide lists the breaking changes from the
published `v2.3.0` contract and the steps required to update an MCP client,
prompt, skill, or integration before installing Kioku 3.

## Who needs to migrate

You need to update your integration if it calls a tool by name, assumes every
tool is present after discovery, or parses the server's legacy text-only
results. Vault Markdown remains the durable storage format, and the Obsidian
bridge continues to negotiate protocol version 3 independently from server
SemVer.

## Breaking changes

Kioku 3 removes, renames, or consolidates public MCP tools that were available
in `v2.3.0`. The old names are not advertised by the Kioku 3 discovery
contract.

### Tool names

Use the following replacements when updating a client:

| Kioku 2.3.0 tool | Kioku 3 tool or action | Migration action |
| --- | --- | --- |
| `create_plan` | `create_implementation_plan` | Replace the tool name and use the focused `objective`, `steps`, and `project` fields. |
| `list_deleted_notes` | `manage_trash(action="list")` | Replace the tool call and move filters and pagination to `prefix`, `limit`, and `offset`. |
| `restore_note_from_trash` | `manage_trash(action="restore")` | Replace the tool call and pass the source in `note`; use `destination` when needed. |
| `restore_note_version` | No direct Kioku 3 tool | Use the vault's existing Git or backup workflow for note-version recovery. |
| `revert_note` | No Kioku 3 tool | Use `git restore -- <path>` after reviewing the change. |
| `revert_all_uncommitted` | No Kioku 3 tool | Use native Git commands after reviewing the complete vault diff. |
| `apply_css_snippet` | `manage_css_snippets(action="apply")` | Pass the snippet filename as `name`, CSS as `css_content`, and the enabled state as `enable`. |
| `list_css_snippets` | `manage_css_snippets(action="list")` | Replace the tool name and remove the old tool-specific arguments. |
| `remove_css_snippet` | `manage_css_snippets(action="remove")` | Pass the snippet filename as `name`. |
| `commit_staged` | No Kioku 3 tool | Use the native Git CLI for repository operations. |
| `get_git_status` | No Kioku 3 tool | Use the native Git CLI for repository operations. |
| `stage_note` | No Kioku 3 tool | Use the native Git CLI for repository operations. |
| `unstage_note` | No Kioku 3 tool | Use the native Git CLI for repository operations. |

The complete legacy `GitTools` surface is not part of Kioku 3 discovery. Do
not use the old names as fallback calls after `tools/list`.

### Focused creation tools

Kioku 3 keeps `create_note` and `create_project_doc` as deprecated
compatibility wrappers, but new integrations must use focused tools. Use
`record_adr`, `record_bug`, `create_implementation_plan`,
`save_project_knowledge`, and the focused note-creation tools for new code.

Later compatible Kioku 3 minor releases can add new focused tools. Treat live
`tools/list` and the generated contract for the version you are running as
authoritative rather than freezing an integration to the original 3.0.0 tool
inventory.

### Capability profiles

The original Kioku **3.0.0** contract does not expose every capability in the
default discovery profile. Its baseline contains 44 tools in the default
profile and 77 tools in the all-capabilities profile. Later compatible Kioku 3
minor releases may add tools without changing the migration requirements from
2.3.0.

The following groups are disabled by default:

- `research`
- `generation`
- `css`
- `assets`
- `bridge`
- `plugin`
- `coordination`

Clients must treat `tools/list` as authoritative and must not assume that an
optional tool exists. Call `get_server_capabilities` when the integration needs
to inspect the coordination profile or rollout state.

To use an explicit capability allowlist, configure the vault and restart the
server:

```yaml
capabilities:
  require_explicit: true
  enabled: [tasks, organization, sessions, workflows, graph, engineering]
```

Add optional groups only when the integration requires them. Coordination is
implemented but remains disabled by default.

### Result and mutation contracts

Many Kioku 3 tools return structured envelopes containing `success`, `data`,
`error`, and `warnings`. Clients must inspect the structured result instead of
using only the display text.

Write tools can accept expected SHA-256 revisions or hashes and mutation IDs.
Coordination-enabled clients can also use resource keys, claims, and fencing
generations. A stale revision or fence is a typed conflict and must be handled
as a retry or reconciliation path, not treated as a successful write.

When coordination is disabled, a call that supplies coordination identifiers
receives `COORDINATION_DISABLED`; it does not create or link a session.

## Runtime and bridge behavior

Core note, search, project, session, indexing, and coordination operations run
directly against the configured vault filesystem. Obsidian does not need to be
open for those operations. UI and plugin operations remain behind the optional
`bridge` and `plugin` capability groups.

The bridge protocol remains version 3 and is negotiated independently from the
server version. The server and the independently released
`sandovaldavid/kioku-obsidian` plugin do not need matching SemVer values.

## Upgrade steps

Complete these steps before switching a production client to Kioku 3.

1. Inventory every tool name used by your prompts, skills, tests, and client
   code.
2. Replace removed and renamed tools using the migration table above.
3. Update discovery logic so it handles optional capability groups and does not
   hard-code the original 3.0.0 profile count.
4. Update result handling to read structured envelopes and stable error codes.
5. Add expected revisions or hashes to read-modify-write workflows, and use a
   mutation ID when retrying the same logical mutation.
6. If you use CSS, bridge, plugin, research, generation, assets, or
   coordination tools, enable the corresponding capability group explicitly.
7. Install `3.0.0` or later and run your `tools/list`, stdio, or Streamable HTTP
   smoke checks against the installed package.

No bulk rewrite of existing Markdown notes is required by the Kioku 3 tool
contract. Back up the vault before changing capability configuration or
running write-heavy migration scripts.

## Kioku 3 compatible evolution

The MCP C# SDK migration and later feature work can ship in Kioku 3 without a
major bump when the public change is backward compatible.

Examples of compatible minor/patch evolution include:

- upgrading an implementation dependency while preserving the public contract;
- adding an optional tool or optional input field;
- adding an optional response field or capability;
- fixing behavior while preserving documented caller expectations.

A **breaking** tool removal/rename, required-field change, incompatible schema
change, or deliberate semantic change to an existing public contract requires
a major release (or an explicitly versioned compatibility mechanism where the
contract defines one).

Therefore the 44/77 counts above are historical 3.0.0 baseline evidence, not a
promise that every Kioku 3 minor release has exactly those counts.

## Related references

Use these references for the current runtime contract and configuration
details.

- [MCP contract reference](commands-reference.md)
- [Focused-tool migration](focused-tool-migration.md)
- [Engineering workflows](engineering-workflows.md)
- [Vault configuration](vault-config.md)
- [Versioning policy](versioning.md)