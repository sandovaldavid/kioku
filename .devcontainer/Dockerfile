ARG DOTNET_VARIANT=10.0-noble
FROM mcr.microsoft.com/devcontainers/dotnet:2-${DOTNET_VARIANT}

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        bat \
        ca-certificates \
        curl \
        fd-find \
        fzf \
        gnupg2 \
        jq \
        less \
        openssh-client \
        python3 \
        ripgrep \
        shellcheck \
        unzip \
    && ln -sf /usr/bin/batcat /usr/local/bin/bat \
    && ln -sf /usr/bin/fdfind /usr/local/bin/fd \
    && rm -rf /var/lib/apt/lists/*

ENV PATH="/home/vscode/.dotnet/tools:/home/vscode/.local/bin:${PATH}"
