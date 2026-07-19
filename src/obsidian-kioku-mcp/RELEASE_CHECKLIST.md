# Plugin release checklist

## Automated

- [ ] `pnpm install --frozen-lockfile`
- [ ] `pnpm --filter obsidian-kioku-mcp run check:compatibility`
- [ ] `pnpm --filter obsidian-kioku-mcp run lint`
- [ ] `pnpm --filter obsidian-kioku-mcp run format:check`
- [ ] `pnpm --filter obsidian-kioku-mcp run test`
- [ ] `pnpm --filter obsidian-kioku-mcp run build`
- [ ] `pnpm --filter obsidian-kioku-mcp run validate:release`
- [ ] Confirm `package.json`, `manifest.json`, and `versions.json` agree.

## Manual test vault

- [ ] Enable the plugin in a clean desktop vault on the minimum supported Obsidian version.
- [ ] Record plugin load time using Obsidian's startup stopwatch.
- [ ] Confirm the listener starts only after layout readiness.
- [ ] Change port and token and confirm automatic restart with no duplicate listener.
- [ ] Verify running/stopped, auth, client, and protocol status.
- [ ] Verify safe editor commands, third-party-denied defaults, and explicit permission toggles.
- [ ] Verify graceful behavior when Dataview, Templater, or Linter is absent.

## Assets and publication

- [ ] Attach `main.js`, `manifest.json`, and `styles.css` to the matching GitHub release tag.
- [ ] Confirm `main.js` is minified, has no source map, and is within 512 KiB.
- [ ] Review README security limitations and troubleshooting.
- [ ] Update plugin-specific changelog and release notes.
- [ ] Do not submit to the Community directory until the independent repository work in #258 is complete.
