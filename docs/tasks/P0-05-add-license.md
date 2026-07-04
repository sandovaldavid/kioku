# P0-05 — Add LICENSE file

| Field | Value |
|---|---|
| Priority | P0 |
| Branch | `chore/add-license` |
| Commit | `chore(config): add MIT license file and metadata` |
| Size | S |
| Dependencies | **Author's decision**: confirm the license is MIT |

## Context

Found during the 2026-07-02 review: the root README states "MIT — see `LICENSE`"
but **the `LICENSE` file does not exist** at the repo root, and no metadata declares it
(`package.json` has no `license` field, csproj has no `PackageLicenseExpression`). The
package is published on NuGet.org (`publish-nuget` in `release-please.yml`) and the plugin
aims for the Community Store — both require an explicit license.

## Scope

1. Confirm the license (the README says MIT).
2. Create `LICENSE` at the root (MIT text, copyright David Sandoval).
3. Declare it in metadata:
   - `src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj`: `<PackageLicenseExpression>MIT</PackageLicenseExpression>`
   - Root `package.json` and `src/obsidian-kioku-mcp/package.json`: `"license": "MIT"`

## Acceptance criteria

- [ ] `LICENSE` exists and the README link resolves.
- [ ] `dotnet pack` includes the license with no warnings.
- [ ] GitHub detects the license on the repo page.
