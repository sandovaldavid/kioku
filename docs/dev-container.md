# Dev Container development environment

Kioku provides a reproducible VS Code Dev Container for the .NET 10 MCP server. Node.js 24 and pnpm are also provisioned for the repository's root-level tooling (commitlint, husky, and the docs-generation script). The container is a development environment only; it is not the production or release image.

The configuration adapts the reusable profiles from `sandovaldavid/dotfiles` to Kioku. It uses the same managed shell lifecycle, pinned terminal tools, persistent history, non-root ownership checks, SSH-signing integration, and canonical Starship prompt while retaining Kioku's hybrid toolchain, shortcuts, extensions, and HTTP port.

## Included toolchain

- Official Microsoft .NET 10 Dev Container image based on Ubuntu 24.04 (`noble`).
- Non-root `vscode` user with host UID/GID synchronization on Linux.
- Node.js 24 and pnpm 11.9.0 through the official Node Dev Container Feature.
- GitHub CLI, Git, SSH and GPG clients, Python 3, `jq`, and `shellcheck`.
- Zsh without Oh My Zsh, using checksum-verified pinned plugins from the dotfiles template.
- Starship 1.26.0 and eza 0.23.5 installed by the shared shell configurator.
- Terminal utilities used by Kioku: `bat`, `fd`, `fzf`, and `ripgrep`.
- VS Code extensions for C#, TypeScript, ESLint, Prettier, EditorConfig, TOML, YAML, and ShellCheck.
- Port forwarding for the optional Streamable HTTP transport on port `5173`.

Dev Container Feature versions and integrity hashes are committed in `.devcontainer/devcontainer-lock.json`. Do not edit that lockfile manually. Use `devcontainer outdated` and `devcontainer upgrade` when intentionally updating Features.

## Template structure

The project follows the current dotfiles template layout:

```text
.devcontainer/
├── Dockerfile
├── devcontainer.json
├── devcontainer-lock.json
├── config/
│   ├── shell.bash
│   ├── shell.zsh
│   └── starship.toml
└── scripts/
    ├── configure-git-ssh-signing.sh
    ├── configure-shell.sh
    ├── initialize-host.sh
    ├── post-create.sh
    ├── post-start.sh
    ├── validate-devcontainer.sh
    └── verify-environment.sh
```

The Dockerfile contains image-level operating-system dependencies. The Node Feature owns Node.js and pnpm. `postCreateCommand` installs the managed shell components and restores project dependencies. Project-specific commands remain in the repository-owned shell profiles under `.devcontainer/config/`.

## Open the repository

1. Install Docker Engine or Docker Desktop, VS Code, and the Dev Containers extension.
2. Clone the repository.
3. Open the repository in VS Code.
4. Run **Dev Containers: Reopen in Container**.

Before the container is created, `initializeCommand` runs `.devcontainer/scripts/initialize-host.sh` on the host. It normalizes directory traversal, file readability, and executable permissions for lifecycle scripts before the repository is exposed through the bind mount. This is required on hardened Linux hosts and rootless container runtimes where permission changes from inside the container can be rejected.

On Linux hosts with SELinux enforcing, the configuration disables SELinux label confinement for this trusted development container so the automatic workspace bind mount remains readable. This does not run the container as `root`, enable privileged mode, or affect production images.

The first creation then:

1. installs Starship 1.26.0 and eza 0.23.5;
2. installs the pinned Zsh plugins with SHA-256 verification;
3. installs managed Bash and Zsh blocks under the user's home directory;
4. verifies the complete Kioku toolchain;
5. runs `CI=true pnpm install --frozen-lockfile`;
6. restores `Kioku.slnx`.

Rebuild the container after changing the Dockerfile, Features, or shell installer. Rerun `bash .devcontainer/scripts/configure-shell.sh` after changing only files under `.devcontainer/config/`.

## Personalized shell

VS Code opens Zsh by default. Repository source files live in `.devcontainer/config/`; `configure-shell.sh` copies them to the user's home configuration and maintains replaceable blocks in `.zshrc` and `.bashrc`.

The profile includes:

- project-scoped persistent command history at `/commandhistory/.zsh_history`;
- autosuggestions from history;
- syntax highlighting and additional completions;
- substring history search with the arrow keys and `Ctrl-P`/`Ctrl-N`;
- eza aliases from the shared dotfiles contract;
- the canonical Dev Container Starship prompt from `sandovaldavid/dotfiles`.

`.devcontainer/config/starship.toml` is synchronized byte-for-byte with the dotfiles template. It deliberately does not define a custom top-level `format`, palette, or Kioku-specific module, so Starship retains its standard module ordering. The resulting prompt follows this shape:

```text
kioku on  <branch> is 󰏗 <package-version> via  <dotnet-version> via  <node-version>
  [Docker] ❯
```

Modules appear only when Starship detects the corresponding project files or runtime. For example, the package, .NET, Node.js, or Python sections may be omitted when they do not apply.

For the intended icon rendering, install **CaskaydiaCove Nerd Font** on the host. VS Code falls back to Cascadia Code or the system monospace font when it is unavailable.

Project shortcuts are available in managed Zsh and Bash sessions:

| Command | Action |
| --- | --- |
| `croot` | Change to the Kioku repository root. |
| `krestore` | Restore the .NET solution. |
| `kbuild` | Build the solution in Release mode. |
| `ktest` | Run the solution tests in Release mode. |
| `kformat` | Apply the repository's .NET whitespace and style formatters. |
| `kverify` | Verify the Dev Container user, shell, credentials, and toolchain. |

The project intentionally omits workstation-only VPN helpers, Bitwarden-specific socket paths, AI CLI installers, and personal or corporate Git identities from the reusable dotfiles repository.

## Lifecycle

- `initializeCommand` runs on the host and normalizes `.devcontainer` permissions before the bind mount is used.
- `postCreateCommand` installs the managed shell profile and restores repository dependencies.
- `postStartCommand` validates UID/GID ownership, repairs non-writable generated paths, and refreshes Git SSH-signing integration.
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

The VS Code Dev Containers extension reuses the host Git configuration and forwards the SSH agent. The container deliberately does not mount `~/.ssh`, force a machine-specific socket, or embed credentials.

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
eza --version
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

If VS Code Server reports `EACCES` while scanning `/workspaces/kioku` on a Linux host, check the host SELinux mode with `getenforce` and inspect recent AVC denials with `ausearch -m avc -ts recent`. The Dev Container uses `--security-opt label=disable` for this host integration issue. Do not replace it with `remoteUser: "root"` or `privileged: true`: those settings do not address the SELinux label mismatch and reduce the container's security and ownership guarantees.

If the prompt does not render icons, verify the host terminal font. If shell files changed, rerun `bash .devcontainer/scripts/configure-shell.sh` and open a new terminal.

If Git identity is missing, confirm it exists on the same host environment from which VS Code was launched. If SSH identities are missing, start the host agent and reopen the container. Opening the image manually with `docker run`, `docker compose up`, or `docker exec` does not reproduce every integration provided by **Reopen in Container**.
