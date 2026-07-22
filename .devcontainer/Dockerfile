ARG DOTNET_VARIANT=2-10.0-noble
ARG STARSHIP_VERSION=1.26.0

FROM mcr.microsoft.com/devcontainers/dotnet:${DOTNET_VARIANT}

ARG STARSHIP_VERSION

USER root

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        bat \
        ca-certificates \
        curl \
        eza \
        fd-find \
        fzf \
        gnupg2 \
        jq \
        less \
        openssh-client \
        python3 \
        ripgrep \
        shellcheck \
        zsh-autosuggestions \
        zsh-syntax-highlighting \
    && ln -sf /usr/bin/batcat /usr/local/bin/bat \
    && ln -sf /usr/bin/fdfind /usr/local/bin/fd \
    && rm -rf /var/lib/apt/lists/*

RUN set -eux; \
    architecture="$(dpkg --print-architecture)"; \
    case "${architecture}" in \
        amd64) starship_target="x86_64-unknown-linux-musl" ;; \
        arm64) starship_target="aarch64-unknown-linux-musl" ;; \
        *) echo "Unsupported architecture for Starship: ${architecture}" >&2; exit 1 ;; \
    esac; \
    archive="starship-${starship_target}.tar.gz"; \
    release_url="https://github.com/starship/starship/releases/download/v${STARSHIP_VERSION}"; \
    cd /tmp; \
    curl -fsSLO "${release_url}/${archive}"; \
    curl -fsSLO "${release_url}/${archive}.sha256"; \
    sha256sum -c "${archive}.sha256"; \
    tar -xzf "${archive}"; \
    install -m 0755 starship /usr/local/bin/starship; \
    rm -f starship "${archive}" "${archive}.sha256"

USER vscode

ENV PATH="/home/vscode/.dotnet/tools:/home/vscode/.local/bin:${PATH}"
