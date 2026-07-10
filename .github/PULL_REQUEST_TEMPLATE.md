## Summary

<!-- What does this PR do, and why? -->

## Test plan

<!-- How did you verify this works? Be specific: commands run, manual testing steps, etc. -->

- [ ] `dotnet build src/Kioku.Mcp.Server/` succeeds (if server changed)
- [ ] `dotnet test src/Kioku.Mcp.Server.Tests/` passes (if server changed)
- [ ] `dotnet format src/Kioku.Mcp.Server/ --verify-no-changes` is clean (if server changed)
- [ ] `pnpm lint:plugin` / `pnpm build:plugin` pass (if plugin changed)
- [ ] `docs/commands-reference.md` regenerated via `scripts/GenerateCommandsRef` (if a tool was added/renamed/changed)
- [ ] Manually tested against a real vault (for UI/behavior changes that automated tests can't cover)

## Checklist

- [ ] Commit messages follow `type(scope): description` (scope is one of `server`, `plugin`, `docs`, `ci`, `config`, `deps`, `release`)
- [ ] Targets `develop`, not `main`
- [ ] Updated relevant docs (README, `docs/install.md`, `docs/vault-config.md`) if env vars or capability groups changed
