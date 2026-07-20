# CI quality and release gates

Kioku treats CI as release evidence rather than only a compilation check. Protected branches
must prove that the server works through the same installation and transport paths used by MCP
clients.

## Native test coverage

The complete `Kioku.Mcp.Server.Tests` suite runs on:

- Ubuntu
- Windows
- macOS

A separate filesystem-security matrix remains in place for the vault sandbox, external-read
boundaries, symlink or reparse-point behavior, note helpers, and permanent-delete policy. Keeping
that focused matrix makes filesystem regressions easier to diagnose even though those tests also
run as part of the complete native suite.

## Distribution smoke tests

Every supported CI operating system performs two end-to-end flows through the official MCP client
SDK:

1. Pack `kioku-mcp-server` into an isolated local NuGet feed, install it into a clean tool path,
   launch the installed `kioku` command over stdio, complete initialization and `tools/list`, then
   create, read, and delete a note. Kioku itself is resolved from the local feed while its
   transitive dependencies are resolved through an explicit NuGet.org package-source mapping.
2. Resolve the runner's native RID, publish a self-contained single-file server, launch it with
   authenticated Streamable HTTP, wait for readiness, and repeat the same MCP read/write flow.

The smoke client lives in `scripts/Kioku.Ci`. It deliberately starts real processes and does not
replace the transport with in-memory test doubles.

## Coverage policy

The reviewed baseline is **40% line coverage** for packages whose assembly name starts with
`Kioku.Mcp.Server`. The blocking gate is implemented by `scripts/verify-coverage.py` against the
Cobertura report produced on Ubuntu.

This baseline is intentionally conservative while legacy services are decomposed. It must not be
reduced in unrelated pull requests. Raise it incrementally when new tests or architectural splits
make a higher threshold stable.

Codecov remains informational because pull requests from forks cannot reliably access repository
secrets. A Codecov outage therefore does not block contributions; the repository-local coverage
gate still blocks protected branches.

## Security and dependency evidence

- `dotnet list package --vulnerable --include-transitive` blocks known .NET vulnerabilities.
- `pnpm audit --audit-level=high` blocks high-severity plugin dependency findings.
- Dependency Review rejects new high-severity dependency regressions on release PRs targeting the
  default `main` branch. Feature PRs targeting `develop` remain blocked by the .NET and pnpm
  vulnerability audits because GitHub's dependency-review API is default-branch oriented.
- CodeQL analyzes C# and JavaScript/TypeScript on pushes, pull requests, and a weekly schedule.
- CI uploads complete .NET and pnpm package inventories for 30 days. These inventories are the
  current reproducible dependency evidence; a signed SPDX or CycloneDX SBOM can replace them when
  release signing and provenance are introduced.

## Local verification

Run the repository checks before opening a pull request:

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
pnpm install --frozen-lockfile
pnpm --filter obsidian-kioku-mcp run lint
pnpm --filter obsidian-kioku-mcp run test
```

The installed-tool and native-binary smoke tests are defined in `.github/workflows/ci.yml` because
they require clean per-OS tool paths and native RIDs.
