# Versioning Policy

> Generated from `docs/public-metadata.json`. Do not edit manually.
> Verify: `node scripts/generate-public-docs.mjs --check`

## Server

Current server package version: **2.3.0**. The NuGet PackageVersion is authoritative for the server and must match both version fields in the MCP server manifest.

## Obsidian plugin

The Obsidian plugin lives in its own repository (sandovaldavid/kioku-obsidian) with its own release pipeline and SemVer, fully independent from server SemVer. This repository no longer tracks plugin version files.

## Root workspace

The private root package.json is an unversioned workspace coordinator and is not a release artifact.

## Bridge compatibility

The bridge protocol currently supports version 3 only (BridgeProtocol.MinVersion = BridgeProtocol.MaxVersion = 3 in ObsidianBridgeService.cs); the min/max range mechanism exists for a future backward-compatibility window, but none is open today. Compatibility is negotiated at connection time via the `auth` handshake, where the server sends its supported min/max version range and the plugin responds with the negotiated version and granted capabilities — not by requiring the server and plugin to share a product version number. This replaces the same-version coupling that existed when server and plugin shipped from a single repository. The canonical wire-format examples for this handshake and for runtime request/response messages live in `src/Kioku.Mcp.Server.Tests/Fixtures/BridgeProtocol/` and are covered by `BridgeProtocolFixtureTests`; the plugin repository (sandovaldavid/kioku-obsidian) keeps its own copy of the same fixtures validated by its own test suite, so a breaking wire-format change fails locally on whichever side made it, without needing a shared package or cross-repo CI.
