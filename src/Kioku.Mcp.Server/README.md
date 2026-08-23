# Kioku MCP Server

> Current package version: **3.1.3** <!-- x-release-please-version --> · [Documentation](https://sandovaldavid.github.io/kioku/) · [Release notes](https://github.com/sandovaldavid/kioku/releases) · [Source](https://github.com/sandovaldavid/kioku)

Kioku is a local-first Model Context Protocol server that gives AI agents persistent, structured memory in an Obsidian vault. It lets MCP clients read, search, write, and organize ordinary Markdown and YAML-frontmatter files while keeping the vault under the user's control.

The server works headlessly: Obsidian does not need to be open for core note, search, project, indexing, work-session, or coordination operations. The separately released [Kioku Obsidian plugin](https://github.com/sandovaldavid/kioku-obsidian) is optional and is required only for UI-aware commands and supported-plugin bridge operations.

## Install or update

Install Kioku as a global .NET tool:

```bash
dotnet tool install --global kioku-mcp-server
```

Update an existing installation:

```bash
dotnet tool update --global kioku-mcp-server
```

Verify that the installed command is on `PATH` without starting the MCP process:

```bash
command -v kioku
```

On PowerShell:

```powershell
Get-Command kioku
```

Kioku requires the path to an Obsidian vault before the server starts. For a direct local launch:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
kioku
```

`stdio` is the default transport and is intended for desktop and CLI MCP clients. Kioku also supports authenticated Streamable HTTP for long-running or shared deployments.

## Register an MCP client

A minimal client configuration starts `kioku` and supplies the vault path:

```json
{
  "mcpServers": {
    "kioku": {
      "command": "kioku",
      "env": {
        "KIOKU_VAULT_PATH": "/absolute/path/to/your/vault"
      }
    }
  }
}
```

The repository documents native MCP registration for Claude Code, Codex, OpenCode, GitHub Copilot, and Antigravity, plus native plugin bundles where the client supports that model. See the [installation guide](https://sandovaldavid.github.io/kioku/install.html) for client-specific commands/configuration, Docker, source builds, and troubleshooting.

## What Kioku provides

- durable project context, first-class engineering specs, implementation plans, decisions, bugs, knowledge, and session handoffs;
- explicit SPEC → PLAN relationships while keeping external coding methodologies outside the Kioku runtime;
- bounded vault reads and guarded writes with structured error results;
- full-text retrieval plus optional local Ollama embeddings and generation;
- focused engineering and organization tools backed by readable Markdown;
- capability-gated optional groups, including CSS, assets, bridge, plugin, and coordination;
- local `stdio` and authenticated Streamable HTTP transports;
- an optional Obsidian bridge without coupling server and plugin SemVer.

The default discovery profile contains the safe core surface. Optional capability groups must be enabled explicitly when an integration needs them. Treat `tools/list` as authoritative rather than assuming every possible tool is registered.

For the durable project model, including spec lifecycle, SPEC → PLAN linking, core/eager project folders, optional/lazy `daily` and `tickets`, and revision behavior, see [Engineering workflows](https://sandovaldavid.github.io/kioku/engineering-workflows.html).

## Kioku 3 migration

Kioku 3 changed public tool names, discovery profiles, structured results, and guarded mutation behavior. Integrations upgrading from `2.3.0` should follow the [2.3.0 to 3.0.0 migration guide](https://sandovaldavid.github.io/kioku/migration-2.3.0-to-3.0.0.html) before switching production clients.

The authoritative generated references are:

- [MCP contract reference](https://sandovaldavid.github.io/kioku/commands-reference.html)
- [Server configuration reference](https://sandovaldavid.github.io/kioku/configuration-reference.html)
- [Vault configuration](https://sandovaldavid.github.io/kioku/vault-config.html)
- [Versioning and bridge compatibility](https://sandovaldavid.github.io/kioku/versioning.html)

## Security defaults

- writes are constrained to the configured vault;
- external reads and permanent deletion are disabled by default;
- Streamable HTTP binds to loopback by default;
- non-loopback HTTP requires an API key unless an explicit unsafe override is enabled;
- browser origins and trusted proxies use exact allowlists;
- bridge authentication uses a separate shared token;
- optional local AI calls use the configured Ollama service.

Review the [threat and privacy model](https://sandovaldavid.github.io/kioku/threat-and-privacy-model.html) and [Streamable HTTP security guide](https://sandovaldavid.github.io/kioku/deploy/auth-options.html) before exposing Kioku outside a trusted local environment.

## License

Kioku is released under the [MIT License](https://github.com/sandovaldavid/kioku/blob/main/LICENSE).
