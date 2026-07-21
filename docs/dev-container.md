# Dev Container development environment

Kioku provides a reproducible VS Code Dev Container for the .NET 10 MCP server and the Node.js 24 Obsidian plugin. The container is a development environment only; it is not the production or release image.

## Included toolchain

- Official Microsoft .NET 10 Dev Container image based on Ubuntu 24.04 (`noble`).
- Non-root `vscode` user with host UID/GID synchronization on Linux.
- Node.js 24 and pnpm 11.9.0 through the official Node Dev Container Feature.
- GitHub CLI, Git, SSH client, GPG client, Python 3, `jq`, and `shellcheck`.
- VS Code extensions for C#, TypeScript, ESLint, Prettier, EditorConfig, YAML, and ShellCheck.
- Port forwarding for the optional Streamable HTTP transport on port `5173`.

Feature versions and integrity hashes are committed in `.devcontainer/devcontainer-lock.json`. Do not edit that file manually. Use `devcontainer outdated` and `devcontainer upgrade` when intentionally updating Features.

## Open the repository

1. Install Docker Engine or Docker Desktop, VS Code, and the Dev Containers extension.
2. Clone the repository.
3. Open the repository in VS Code.
4. Run **Dev Containers: Reopen in Container**.

The first creation restores `Kioku.slnx` and installs the plugin dependencies with `pnpm install --frozen-lockfile`. Rebuild the container after changing any file under `.devcontainer/`.

## Git and SSH credentials

Keep personal identity, credentials, and private keys on the host. Do not add them to `devcontainer.json`, the Dockerfile, or repository environment files.

Configure Git on the host before opening the container:

```bash
git config --global user.name "Your Name"
git config --global user.email "you@example.com"
```

For SSH authentication, start the host agent and load the key:

```bash
eval "$(ssh-agent -s)"
ssh-add ~/.ssh/id_ed25519
ssh-add -l
```

The VS Code Dev Containers extension reuses the host Git configuration and forwards the SSH agent. The container deliberately does not mount `~/.ssh` or embed credentials.

## Validate the environment

Run the fast in-container diagnostic:

```bash
bash .devcontainer/scripts/verify-environment.sh
```

Run a clean Dev Container build and all repository quality gates from the host:

```bash
bash .devcontainer/scripts/validate-devcontainer.sh
```

The validation script requires the Dev Container CLI and uses the committed Feature lockfile in frozen mode.

## Optional local services

Kioku does not start Ollama or Obsidian as sidecars because both are optional host applications. When semantic search uses a host Ollama instance, configure `KIOKU_OLLAMA_URL` for the host address supported by your container runtime. Do not commit machine-specific vault paths, API keys, or local service credentials.

The Obsidian bridge listens from the Obsidian application and remains optional. The Streamable HTTP server is the only container port forwarded by default.

## Troubleshooting

Inside the container, verify the effective user and credentials integration:

```bash
whoami
id
echo "$HOME"
git config --global --show-origin --list
echo "$SSH_AUTH_SOCK"
ssh-add -l
git remote -v
```

If Git identity is missing, confirm it exists on the same host environment from which VS Code was launched. If SSH identities are missing, start the host agent and reopen the container. Opening the image manually with `docker run`, `docker compose up`, or `docker exec` does not reproduce every integration provided by **Reopen in Container**.
