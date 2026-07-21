ARG DOTNET_VARIANT=2-10.0-noble
FROM mcr.microsoft.com/devcontainers/dotnet:${DOTNET_VARIANT}

USER root

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        gnupg2 \
        jq \
        openssh-client \
        python3 \
        shellcheck \
    && rm -rf /var/lib/apt/lists/*

USER vscode
