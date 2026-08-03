# Dev Container development environment

Kioku provides a reproducible VS Code Dev Container for the .NET 10 MCP server. Node.js 24 is
available for the documentation generator and integration checks. The container is a development
environment only; it is not the production or release image.

## Included toolchain

- Official Microsoft .NET 10 Dev Container image based on Ubuntu 24.04 (`noble`).
- Non-root `vscode` user with host UID/GID synchronization on Linux.
- Zsh as the default shell with the customized Starship prompt from
  `.devcontainer/config/starship.toml`.
- Node.js 24 through the official Node Dev Container Feature.
- GitHub CLI, Git, SSH and GPG clients, Python 3, `jq`, `ripgrep`, and `shellcheck`.
- VS Code extensions for C#, TypeScript, ESLint, Prettier, EditorConfig, TOML, YAML, and ShellCheck.
- Port forwarding for the optional Streamable HTTP transport on port `5173`.

Dev Container Feature versions and integrity hashes are committed in
`.devcontainer/devcontainer-lock.json`. Do not edit that lockfile manually. Use `devcontainer
outdated` and `devcontainer upgrade` when intentionally updating Features.

## Structure

```text
.devcontainer/
├── Dockerfile
├── devcontainer.json
├── devcontainer-lock.json
├── config/
│   ├── .zshrc
│   └── starship.toml
└── scripts/
    ├── post-create.sh
    └── validate-devcontainer.sh
```

The Dockerfile contains image-level operating-system dependencies. The official Features install
the .NET base tooling, Node.js, GitHub CLI, Zsh, and Starship. The repository-owned `.zshrc` points
Starship at the customized configuration and does not install plugins or define project-specific
aliases. `postCreateCommand` restores the solution after the container is created.

The prompt keeps the repository's customized symbols and git-status formatting. Its normal shape is
the Starship default module order with Kioku's directory, branch, package, .NET, Node.js, Docker,
and status modules shown when the matching project state is present.

Install **CaskaydiaCove Nerd Font** on the host for the configured symbols. VS Code falls back to
Cascadia Code or the system monospace font when it is unavailable.

## Open the repository

1. Install Docker Engine or Docker Desktop, VS Code, and the Dev Containers extension.
2. Clone the repository.
3. Open the repository in VS Code.
4. Run **Dev Containers: Reopen in Container**.

The first creation restores `Kioku.slnx`. No root package-manager install is required; repository
documentation checks run directly with Node.js:

```bash
node scripts/generate-public-docs.mjs --check
```

On Linux hosts with SELinux enforcing, the configuration disables SELinux label confinement for
this trusted development container so the automatic workspace bind mount remains readable. This
does not run the container as `root`, enable privileged mode, or affect production images.

The Dev Container uses `init: true` so orphaned child processes are reaped correctly. Rebuild it
after changing the Dockerfile or Features.

## Git and SSH credentials

Keep personal identity, credentials, and private keys on the host. Do not add them to
`devcontainer.json`, the Dockerfile, or repository environment files.

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

The VS Code Dev Containers extension reuses the host Git configuration and forwards the SSH
agent. The container deliberately does not mount `~/.ssh`, force a machine-specific socket, or
embed credentials.

## Validate the environment

Run the complete Dev Container validation from the host repository root:

```bash
bash .devcontainer/scripts/validate-devcontainer.sh
```

The validation script reads the resolved configuration, builds with the committed Feature
lockfile, starts the container, restores and builds the solution, runs server tests and formatting
checks, verifies .NET vulnerabilities, validates skill frontmatter and generated skill copies,
and runs ShellCheck on repository scripts.

## Optional local services

Kioku does not start Ollama or Obsidian as sidecars because both are optional host applications.
When semantic search uses a host Ollama instance, configure `KIOKU_OLLAMA_URL` for the host address
supported by your container runtime. Do not commit machine-specific vault paths, API keys, or
local service credentials.

The Obsidian bridge listens from the Obsidian application and remains optional. The Streamable HTTP
server is the only container port forwarded by default.

## Troubleshooting

Inside the container, verify the effective user and core tools:

```bash
whoami
id
dotnet --version
node --version
zsh --version
starship --version
gh --version
git remote -v
```

If VS Code Server reports `EACCES` while scanning `/workspaces/kioku` on a Linux host, check the
host SELinux mode with `getenforce` and inspect recent AVC denials with `ausearch -m avc -ts
recent`. The Dev Container uses `--security-opt label=disable` for this host integration issue.
Do not replace it with `remoteUser: "root"` or `privileged: true`.
