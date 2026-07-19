# Changelog

All notable plugin-specific changes are documented here. The plugin currently follows the monorepo release version until extraction into its independent repository.

## Unreleased

### Changed

- Defer bridge startup until the Obsidian workspace layout is ready and report measured startup timing.
- Apply port, token, and permission changes through serialized automatic restarts.
- Use sentence-case, non-redundant command and settings labels.
- Isolate undocumented Obsidian command/plugin registries behind guarded compatibility adapters.
- Use `FileSystemAdapter` with an `instanceof` guard for desktop vault paths.
- Pin Obsidian API typings, align `minAppVersion`, minify production output, and enforce release validation.
