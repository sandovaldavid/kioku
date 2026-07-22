# Dev Container development environment

Kioku provides a reproducible VS Code Dev Container for the .NET 10 MCP server and the Node.js 24 Obsidian plugin. The container is a development environment only; it is not the production or release image.

The configuration adapts the maintainer's reusable dotfiles template to Kioku. It preserves the personalized Zsh and Starship developer experience while keeping the repository self-contained and removing host-specific settings that do not belong in a shared project.

## Included toolchain

- Official Microsoft .NET 10 Dev Container image based on Ubuntu 24.04 (`noble`).
- Non-root `vscode` user with host UID/GID synchronization on Linux.
- Node.js 24 and pnpm 11.9.0 through the official Node Dev Container Feature.
- GitHub CLI, Git, SSH client, GPG client, Python 3, `jq`, and `shellcheck`.
- Zsh with Oh My Zsh, autosuggestions, syntax highlighting, and a Kioku-specific Starship prompt.
- Terminal utilities from the dotfiles workflow: `bat`, `eza`, `fd`, `fzf`, and `ripgrep`.
- VS Code extensions for C#, TypeScript, ESLint, Prettier, EditorConfig, TOML, YAML, and ShellCheck.
- Port forwarding for the optional Streamable HTTP transport on port `5173`.

Starship is pinned by Docker build argument and installed from the matching upstream release archive after checksum verification. Feature versions and integrity hashes are committed in `.devcontainer/devcontainer-lock.json`. Do not edit that lockfile manually. Use `devcontainer outdated` and `devcontainer upgrade` when intentionally updating Features.

## Open the repository

1. Install Docker Engine or Docker Desktop, VS Code, and the Dev Containers extension.
2. Clone the repository.
3. Open the repository in VS Code.
4. Run **Dev Containers: Reopen in Container**.

Before the container is created, `initializeCommand` runs `.devcontainer/scripts/initialize-host.sh` on the host. It normalizes directory traversal, file readability, and executable permissions for lifecycle scripts before the repository is exposed through the bind mount. This is required on hardened Linux hosts and rootless container runtimes where permission changes from inside the container can be rejected.

On Linux hosts with SELinux enforcing, the configuration disables SELinux label confinement for this development container so the automatic workspace bind mount remains readable. This setting applies only to the Dev Container. It does not change the remote user, run the container as `root`, or affect the production image and Compose services.

The first creation then configures the shell, restores `Kioku.slnx`, and installs plugin dependencies with `CI=true pnpm install --frozen-lockfile`. The non-interactive setting lets lifecycle commands recreate stale `node_modules` directories safely. Rebuild the container after changing the Dockerfile, Features, or any other file under `.devcontainer/`.

## Personalized shell

VS Code opens Zsh by default. The prompt configuration lives in `.devcontainer/shell/starship.toml`, and the project shell behavior lives in `.devcontainer/shell/init.zsh`.

The prompt shows:

- the Kioku identity (`記憶 Kioku`);
- the current directory and Git branch;
- detailed Git worktree state using the same Nerd Font symbols as the dotfiles configuration;
- active .NET and Node.js versions;
- command duration and exit status;
- container and SSH context when applicable.

For the intended icon rendering, install **CaskaydiaCove Nerd Font** on the host. VS Code falls back to Cascadia Code or the system monospace font when it is unavailable.

Project shortcuts:

| Command    | Action                                                                    |
| ---------- | ------------------------------------------------------------------------- |
| `croot`    | Change to the Kioku repository root.                                      |
| `krestore` | Restore the .NET solution.                                                |
| `kbuild`   | Build the solution in Release mode.                                       |
| `ktest`    | Run the solution tests in Release mode.                                   |
| `kformat`  | Apply the repository's .NET whitespace and style formatters.              |
| `kplugin`  | Lint, test, and build the Obsidian plugin.                                |
| `kverify`  | Verify the Dev Container user, shell, prompt, credentials, and toolchain. |

The project intentionally does not copy workstation-only aliases, VPN commands, Bitwarden socket paths, AI CLI installers, or personal Git identity from the dotfiles repository.

## Lifecycle

- `initializeCommand` runs on the host and normalizes `.devcontainer` permissions before the bind mount is used.
- `postCreateCommand` runs inside the container, configures the shell, and installs repository dependencies.
- `postStartCommand` validates UID/GID ownership, repairs non-writable generated paths, and refreshes the shell link.
- All scripts are idempotent and terminate after completing their work.
- The Dev Container uses `init: true` so orphaned child processes are reaped correctly.

The host-side initialization can also be executed manually:

```bash
bash .devcontainer/scripts/initialize-host.sh
```

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

The VS Code Dev Containers extension reuses the host Git configuration and forwards the SSH agent. The container deliberately does not mount `~/.ssh`, force a Bitwarden-specific socket, or embed credentials.

## Validate the environment

Run the fast in-container diagnostic:

```bash
kverify
```

Equivalent explicit command:

```bash
bash .devcontainer/scripts/verify-environment.sh
```

Run a clean Dev Container build and all repository quality gates from the host:

```bash
bash .devcontainer/scripts/validate-devcontainer.sh
```

The validation script first executes the host permission initializer, then requires the Dev Container CLI and uses the committed Feature lockfile in frozen mode.

## Optional local services

Kioku does not start Ollama or Obsidian as sidecars because both are optional host applications. When semantic search uses a host Ollama instance, configure `KIOKU_OLLAMA_URL` for the host address supported by your container runtime. Do not commit machine-specific vault paths, API keys, or local service credentials.

The Obsidian bridge listens from the Obsidian application and remains optional. The Streamable HTTP server is the only container port forwarded by default.

## Troubleshooting

Inside the container, verify the effective user and credentials integration:

```bash
whoami
id
echo "$HOME"
echo "$SHELL"
echo "$STARSHIP_CONFIG"
starship --version
git config --global --show-origin --list
echo "$SSH_AUTH_SOCK"
ssh-add -l
git remote -v
```

If a lifecycle script reports `Permission denied`, close the failed container and run this command from the host repository root:

```bash
bash .devcontainer/scripts/initialize-host.sh
```

Then use **Dev Containers: Rebuild Container Without Cache**. Do not try to repair the bind-mounted repository using `sudo chmod` from inside the container; hardened or rootless Linux runtimes can reject those changes because the host owns the mount.

If VS Code Server reports `EACCES` while scanning `/workspaces/kioku` on a Linux host, check the host SELinux mode with `getenforce` and inspect recent AVC denials with `ausearch -m avc -ts recent`. The Dev Container uses `securityOpt: ["label=disable"]` for this host integration issue. Do not replace it with `remoteUser: "root"` or `privileged: true`: those settings do not address the SELinux label mismatch and reduce the container's security and ownership guarantees.

If the prompt does not render icons, verify the host terminal font. If Zsh or Starship settings changed, run `bash .devcontainer/scripts/configure-shell.sh`, open a new terminal, or rebuild the Dev Container.

If Git identity is missing, confirm it exists on the same host environment from which VS Code was launched. If SSH identities are missing, start the host agent and reopen the container. Opening the image manually with `docker run`, `docker compose up`, or `docker exec` does not reproduce every integration provided by **Reopen in Container**.
