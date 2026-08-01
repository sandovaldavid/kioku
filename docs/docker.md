# Docker deployment

The repository provides a root [`Dockerfile`](../Dockerfile) for the .NET server and a root [`docker-compose.yml`](../docker-compose.yml) that runs:

- `kioku-server` on port `5173`;
- `ollama` on port `11434`;
- an `ollama-data` volume;
- a bind mount from `KIOKU_VAULT_PATH` to `/vault`.

The Compose stack selects Streamable HTTP and requires `KIOKU_API_KEY`.

## Validate configuration

```bash
docker compose config
```

Set an absolute vault path and an API key before starting:

```bash
export KIOKU_VAULT_PATH="/absolute/path/to/your/vault"
export KIOKU_API_KEY="$(openssl rand -hex 32)"
docker compose up --build
```

The server is then reachable at:

```text
http://127.0.0.1:5173/mcp
```

Liveness is public and minimal:

```bash
curl -f http://127.0.0.1:5173/health/live
```

Protected MCP and readiness requests must use the configured bearer token.

## Common operations

```bash
docker compose ps
docker compose logs -f kioku-server
docker compose logs -f ollama
docker compose down
```

Rebuild after server or image changes:

```bash
docker compose build --no-cache kioku-server
docker compose up
```

## Vault mount

The default Compose expression is:

```yaml
${KIOKU_VAULT_PATH:-./vault}:/vault
```

Use an absolute host path for predictable behavior and verify that Docker can read and write it. Kioku's internal vault sandbox still applies inside `/vault`.

## Ollama

The supplied Compose file points Kioku to `http://ollama:11434` and currently references `ollama/ollama:latest`. For reproducible deployments, override or pin that image to a version or digest accepted by the operator.

Pull the configured embedding model inside the running service:

```bash
docker compose exec ollama ollama pull "${KIOKU_EMBEDDING_MODEL:-nomic-embed-text}"
```

Keyword search remains available when Ollama is unavailable. Semantic search, embedding generation, and optional generation depend on Ollama.

## Security

- Do not expose port `5173` publicly without reviewing [deploy/auth-options.md](deploy/auth-options.md).
- Keep `KIOKU_API_KEY` outside committed files.
- Restrict the vault mount to the intended directory.
- Do not enable permanent deletion or external reads without an operational requirement.
- Review `KIOKU_HTTP_ALLOWED_ORIGINS` when browser clients are involved.
- The Compose file publishes the Ollama port to the host; remove or restrict that mapping when it is not needed.

## Build only the server image

```bash
docker build -t kioku-server .
docker run --rm \
  -p 5173:5173 \
  -e KIOKU_VAULT_PATH=/vault \
  -e KIOKU_TRANSPORT=http \
  -e KIOKU_HTTP_HOST=0.0.0.0 \
  -e KIOKU_API_KEY="$KIOKU_API_KEY" \
  -v "$KIOKU_VAULT_PATH:/vault" \
  kioku-server
```

The Dockerfile health check calls `/health/live`. For deployment diagnostics, see [troubleshooting.md](troubleshooting.md).
