---
layout: default
title: Documentation
sidebar: true
---

# Kioku documentation

This index describes the documentation that is authoritative for the current target branch. Code, tests, generated MCP discovery, and versioned configuration take precedence over prose when they disagree.

## Status taxonomy

| Status | Meaning |
|---|---|
| **Implemented** | Present in the target branch and supported by code, configuration, tests, or generated contracts. |
| **In progress** | Active work exists but is incomplete on the target branch. |
| **Planned** | Accepted future work without complete implementation. |
| **Blocked** | Work cannot proceed because a named dependency remains unresolved. |
| **Deprecated** | Still present for compatibility but not recommended for new use. |
| **Historical** | Point-in-time context that is not an active contract. |
| **Discarded** | Rejected, superseded, or closed without implementation. |
| **Unconfirmed** | Not provable from accessible source, tests, or repository settings. |

Active repository documentation should normally describe **Implemented** behavior and clearly identified **Deprecated** compatibility paths. Plans, rationale, alternatives, completed execution records, and session handoffs belong in Cortex-L7. Open issues and pull requests are not implementation evidence.

## Start here

| Document | Status | Purpose |
|---|---|---|
| [Installation](install.md) | Implemented | Install the server, register clients, build from source, and verify a deployment. |
| [Architecture](architecture.md) | Implemented | Current component responsibilities and dependency direction. |
| [Durable coordination profile](durable-coordination.md) | Implemented with gated rollout | Coordination domain, state machine, storage boundary, event persistence, claims, leases, fencing, guarded vault mutations, and the gated MCP surface. |
| [Troubleshooting](troubleshooting.md) | Implemented | Diagnose server, HTTP, indexing, Ollama, bridge, and Docker problems. |
| [Focused-tool migration](focused-tool-migration.md) | Implemented / Deprecated | Preferred focused tools and the generic wrappers retained only for compatibility. |

## Runtime contracts

| Document | Status | Purpose |
|---|---|---|
| [MCP commands reference](commands-reference.md) | Implemented / generated | Authoritative tools, schemas, annotations, prompts, resources, and profile counts. |
| [Server configuration reference](configuration-reference.md) | Implemented / generated | Public environment variables and canonical configuration paths. |
| [Vault configuration](vault-config.md) | Implemented | `.kioku/config.yml`, folders, capabilities, templates, domains, and exclusions. |
| [Vault configuration example](vault-config.example.yml) | Implemented | Copyable configuration baseline. |
| [Versioning](versioning.md) | Implemented / generated | Server, plugin, workspace, and bridge compatibility rules. |
| [MCP tool contract](mcp-tool-contract.md) | Implemented | Result envelopes, annotations, errors, and contract expectations. |
| [Work sessions](work-sessions.md) | Implemented | Session identity, ownership, lifecycle, and handoff behavior. |
| [Coordination contracts](coordination-contracts.md) | Implemented with gated rollout | Versioned coordination contracts, append-only event persistence, replay, leases, fencing, conflicts, and the gated MCP surface. |
| [Coordination observability](coordination-observability.md) | Implemented with opt-in exporters | Privacy-preserving logs, bounded metrics, optional W3C activities, and Sentry filtering. |
| [Coordination interoperability](coordination-interoperability.md) | Implemented analysis | Lossy MCP Tasks and future A2A mappings, plus optional CloudEvents guidance. |
| [Coordination rollout](coordination-rollout.md) | Implemented with gated rollout | Capability negotiation, compatibility rules, release gates, and local-filesystem support boundaries. |
| [Indexing pipeline](indexing-pipeline.md) | Implemented | Bounded indexing, synchronization, recovery, and metrics. |

## Security and deployment

| Document | Status | Purpose |
|---|---|---|
| [Threat and privacy model](threat-and-privacy-model.md) | Implemented with named gaps | Trust boundaries, data flows, controls, and residual risks. |
| [Streamable HTTP authentication](deploy/auth-options.md) | Implemented | API keys, origins, proxies, and deployment guidance. |
| [Docker](docker.md) | Implemented | Build and run the supplied Dockerfile and Compose stack. |
| [systemd unit](deploy/kioku.service) | Implemented example | Service unit template that requires operator-specific paths and secrets. |
| [Nginx configuration](deploy/nginx.conf) | Implemented example | Reverse-proxy template that requires operator-specific values. |

## Development and quality

| Document | Status | Purpose |
|---|---|---|
| [CI quality gates](ci-quality-gates.md) | Implemented | Versioned workflows, local equivalents, and evidence expectations. |
| [Dev Container](dev-container.md) | Implemented | Reproducible development environment and validation. |
| [Performance benchmarks](benchmarks.md) | Implemented evidence | Reproducible benchmark methodology and captured results with caveats. |
| [Retrieval evaluation](retrieval-eval.md) | Implemented evidence | Retrieval harness, metrics, fixtures, and interpretation. |
| [Multi-agent handoff demo](multi-agent-handoff-demo.md) | Implemented evidence | Reproducible end-to-end handoff procedure and transcript. |

## Generated files

Do not hand-edit:

- `commands-reference.md`
- `configuration-reference.md`
- `versioning.md`
- `../src/Kioku.Mcp.Server/.mcp/server.json`

Regenerate and verify them with:

```bash
dotnet build Kioku.slnx --configuration Release --no-restore
node scripts/generate-public-docs.mjs --write
node scripts/generate-public-docs.mjs --check
```

## Documentation boundary

The following content is intentionally not maintained as active repository documentation:

- architecture alternatives and decision rationale;
- completed plans and proposals;
- old tool-count snapshots and superseded migrations;
- cross-repository release strategy;
- private operational context and session handoffs.

That material is preserved in the `20-execution/kioku` workspace in Cortex-L7. Git history, merged pull requests, and closed issues remain the public historical record.
