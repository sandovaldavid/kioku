# P0-04 — Sync README and server.json versions with release-please

| Field | Value |
|---|---|
| Priority | P0 |
| Branch | `chore/readme-version-sync` |
| Commit | `chore(release): bump README and server.json versions via release-please extra-files` |
| Size | S |
| Dependencies | None |

## Context

release-please only updates the plugin's `csproj`, `manifest.json` and `package.json`
(`extra-files`). The versions hand-written in `README.md`,
`src/Kioku.Mcp.Server/README.md` and `src/Kioku.Mcp.Server/.mcp/server.json` drift on every
release (the README once said beta.4 and the server README 1.6.2 while the repo was on
beta.8). The 2026-07-02 docs revision fixed them and left
`<!-- x-release-please-version -->` annotations in both READMEs, but **without registering
the files in the config, release-please won't touch them**.

## Scope

1. Add to `extra-files` in **both** configs (`release-please-config.json` and
   `release-please-config.beta.json`):
   - `README.md` (`generic` updater — uses the `x-release-please-version` annotation
     already present)
   - `src/Kioku.Mcp.Server/README.md` (same)
   - `src/Kioku.Mcp.Server/.mcp/server.json` (`json` updater, jsonpaths `$.version` and
     `$.packages[0].version`)
2. Verify the generic updater syntax for `release-please-action@v4` for markdown/json
   files (official release-please "Updating arbitrary files" documentation).

## Acceptance criteria

- [ ] The next release-please release PR on `develop` updates the version in all
  3 files (verifiable in the automated PR's diff).
- [ ] The annotations in the READMEs stay on the same line as the version.
- [ ] The `release-please.yml` pipeline isn't broken (local dry-run or bot PR review).

## Files

- `release-please-config.json`
- `release-please-config.beta.json`
- `README.md`, `src/Kioku.Mcp.Server/README.md`,
  `src/Kioku.Mcp.Server/.mcp/server.json` (only if the annotation syntax needs adjusting)
