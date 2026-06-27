# Kioku Docker Deployment

This directory contains Docker configuration for deploying Kioku MCP Server.

## Quick Start

1. **Set your vault path:**
   ```bash
   export KIOKU_VAULT_PATH=/path/to/your/obsidian/vault
   ```

2. **Optional: Set API key for HTTP authentication:**
   ```bash
   export KIOKU_API_KEY=your-secure-api-key
   ```

3. **Start the services:**
   ```bash
   docker-compose up -d
   ```

4. **Pull the embedding model (first time only):**
   ```bash
   docker exec kioku-ollama ollama pull nomic-embed-text
   ```

5. **Access the server:**
   - HTTP endpoint: `http://localhost:5173`
   - Health check: `http://localhost:5173/health`

## Configuration

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `KIOKU_VAULT_PATH` | yes | — | Path to your Obsidian vault on the host |
| `KIOKU_API_KEY` | no | — | Bearer token for HTTP authentication |
| `KIOKU_EMBEDDING_MODEL` | no | `nomic-embed-text` | Ollama embedding model |
| `KIOKU_HTTP_PORT` | no | `5173` | HTTP server port |

### GPU Support (Optional)

To enable GPU acceleration for Ollama, uncomment the GPU section in `docker-compose.yml`:

```yaml
deploy:
  resources:
    reservations:
      devices:
        - driver: nvidia
          count: 1
          capabilities: [gpu]
```

Requires NVIDIA Container Toolkit.

## Usage

### MCP Client Configuration

Add to your MCP client config:

```json
{
  "mcpServers": {
    "kioku": {
      "type": "sse",
      "url": "http://localhost:5173/mcp",
      "headers": {
        "Authorization": "Bearer your-api-key"
      }
    }
  }
}
```

### Commands

```bash
# Start services
docker-compose up -d

# Stop services
docker-compose down

# View logs
docker-compose logs -f kioku-server

# Rebuild after code changes
docker-compose build --no-cache

# Remove all data (including Ollama models)
docker-compose down -v
```

## Architecture

```
┌─────────────────┐
│  MCP Client     │
│  (Claude Code)  │
└────────┬────────┘
         │ HTTP-SSE
         │ :5173
┌────────▼────────┐
│ Kioku Server    │
│ (ASP.NET Core)  │
└────────┬────────┘
         │ HTTP
         │ :11434
┌────────▼────────┐
│ Ollama          │
│ (Embeddings)    │
└─────────────────┘
```

## Troubleshooting

### Server won't start

Check logs:
```bash
docker-compose logs kioku-server
```

Common issues:
- Vault path doesn't exist or isn't accessible
- Port 5173 already in use
- Ollama not ready yet (wait for health check)

### Ollama model not found

Pull the model:
```bash
docker exec kioku-ollama ollama pull nomic-embed-text
```

### Can't connect from host

Ensure the server is running and healthy:
```bash
curl http://localhost:5173/health
```
