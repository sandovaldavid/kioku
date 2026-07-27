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

Server and plugin compatibility is negotiated through the bridge protocol and capabilities, not by requiring identical product versions.
