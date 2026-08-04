ARG DOTNET_VARIANT=10.0-noble
FROM mcr.microsoft.com/devcontainers/dotnet:2-${DOTNET_VARIANT}

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg2 \
        jq \
        openssh-client \
        python3 \
        ripgrep \
        shellcheck \
        unzip \
    && rm -rf /var/lib/apt/lists/*

ENV PATH="/home/vscode/.dotnet/tools:${PATH}"
