## Summary

<!-- What does this PR do, and why? -->

## Test plan

<!-- List the exact local commands and manual checks used. -->

- [ ] .NET restore/build/tests pass when server code changed
- [ ] `dotnet format` whitespace and style checks pass when C# changed
- [ ] Plugin lint, format check, tests, and build pass when TypeScript changed
- [ ] `node scripts/generate-public-docs.mjs --check` passes when MCP, configuration, manifest, or version metadata changed
- [ ] Manually tested against a disposable or real vault when behavior requires it

## Checklist

- [ ] Commits follow `type(scope): description`
- [ ] PR targets `develop`
- [ ] Public metadata was updated instead of duplicating inventories in READMEs
- [ ] The PR body links its issue with `Closes #<issue>` when appropriate
