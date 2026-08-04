# CI quality and release gates

Kioku treats CI as release evidence rather than only a compilation check. Protected branches must prove that the server works through the same installation and transport paths used by MCP clients.

## Native test coverage

The complete `Kioku.Mcp.Server.Tests` suite runs on:

- Ubuntu
- Windows
- macOS

Filesystem-security tests cover the vault sandbox, external-read boundaries, symlink or reparse-point behavior, note helpers, and permanent-delete policy as part of the complete native suite.

## Distribution smoke tests

Every supported CI operating system performs two end-to-end flows through the official MCP client SDK:

1. Pack `kioku-mcp-server` into an isolated local NuGet feed, install it into a clean tool path, launch the installed `kioku` command over stdio, complete initialization and `tools/list`, then verify `get_server_capabilities`, then create, read, and delete a note. Kioku itself is resolved from the local feed while its transitive dependencies are resolved through an explicit NuGet.org package-source mapping.
2. Resolve the runner's native RID, publish a self-contained single-file server, launch it with authenticated Streamable HTTP, wait for readiness, verify `get_server_capabilities`, and repeat the same MCP read/write flow.

The smoke client lives in `scripts/Kioku.Ci`. It deliberately starts real processes and does not replace the transport with in-memory test doubles.

## Documentation and integration checks

The `validate-integrations` job builds the server and runs `node scripts/generate-public-docs.mjs --check`. That command verifies:

- generated MCP commands, configuration, versioning, and manifest outputs;
- public environment-variable metadata against runtime mappings;
- current Streamable HTTP terminology.

The same job validates JSON manifests, skill frontmatter, generated skill copies, ShellCheck, and dry-run client installation for `claude-code`, `codex`, `opencode`, and `antigravity`.

The separate `docs-links` workflow runs three documentation contracts on pushes and pull requests targeting `main` or `develop`:

- `node scripts/validate-markdown-links.mjs` verifies repository-relative file links in maintained Markdown entry points;
- `node scripts/validate-docs-navigation.mjs` verifies that every sidebar destination exists and has effective Jekyll layout, title, and sidebar metadata;
- `node scripts/validate-release-documentation.mjs` verifies that the package, MCP manifest, Release Please manifest, root README, NuGet README, AGENTS snapshot, versioning page, and site badge use the same server version and remain covered by release automation.

These checks intentionally do not make network requests or claim that external URLs, hosted documentation, anchors, redirects, or third-party services are available.

## Coverage policy

The reviewed baseline is **40% line coverage** for packages whose assembly name starts with `Kioku.Mcp.Server`. The blocking gate is implemented by `scripts/verify-coverage.py` against the Cobertura report produced on Ubuntu.

This baseline is intentionally conservative while legacy services are decomposed. It must not be reduced in unrelated pull requests. Raise it incrementally when new tests or architectural splits make a higher threshold stable.

Codecov remains informational because pull requests from forks cannot reliably access repository secrets. A Codecov outage therefore does not block contributions; the repository-local coverage gate still blocks protected branches.

## Security and dependency evidence

- `dotnet list package --vulnerable --include-transitive` blocks known .NET vulnerabilities.
- Dependency Review rejects new high-severity dependency regressions on release PRs targeting the default `main` branch. Feature PRs targeting `develop` remain blocked by the .NET vulnerability audit because GitHub's dependency-review API is default-branch oriented.
- JavaScript and TypeScript repository tooling (`scripts/`, `.devcontainer/`) is analyzed by CodeQL on pushes, pull requests, and a weekly schedule. The Obsidian plugin has its own CodeQL and dependency-audit coverage in its own repository.
- C# is analyzed through the repository-wide Roslyn and .NET analyzer baseline with code style and warnings-as-errors enforced by the main build.
- CI uploads complete .NET package inventories for 30 days. These inventories are the current reproducible dependency evidence; a signed SPDX or CycloneDX SBOM can replace them when release signing and provenance are introduced.

## Local verification

Run the repository checks before opening a pull request:

```bash
dotnet restore Kioku.slnx
dotnet build Kioku.slnx --configuration Release --no-restore
dotnet test src/Kioku.Mcp.Server.Tests/Kioku.Mcp.Server.Tests.csproj --configuration Release --no-restore
dotnet format Kioku.slnx whitespace --verify-no-changes --no-restore
dotnet format Kioku.slnx style --verify-no-changes --no-restore
node scripts/generate-public-docs.mjs --check
node scripts/validate-markdown-links.mjs
node scripts/validate-docs-navigation.mjs
node scripts/validate-release-documentation.mjs
```

The installed-tool and native-binary smoke tests are defined in `.github/workflows/ci.yml` because they require clean per-OS tool paths and native RIDs.
