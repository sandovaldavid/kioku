---
layout: default
title: Troubleshooting Guide
sidebar: true
---

## Common Issues

### Server Won't Start

#### Error: "KIOKU_VAULT_PATH environment variable is not configured"

**Cause:** The vault path environment variable is not set.

**Solution:**
```bash
export KIOKU_VAULT_PATH=/path/to/your/vault
# Or set it permanently in your shell profile (~/.bashrc, ~/.zshrc, etc.)
```

#### Error: "Vault path does not exist"

**Cause:** The specified vault path doesn't exist or is not accessible.

**Solution:**
```bash
# Verify the path exists
ls -la /path/to/your/vault

# Check permissions
chmod 755 /path/to/your/vault
```

### Plugin Issues

#### Bridge Not Starting

**Symptoms:** No bridge listening message in console, plugin shows as enabled but not working.

**Possible Causes:**
1. Port 7765 already in use
2. Plugin not properly installed
3. Obsidian needs restart

**Solutions:**
```bash
# Check if port is in use
lsof -i :7765

# Kill the process
kill -9 <PID>

# Or change the port in plugin settings
```

#### "Could not start the bridge" Notice

**Cause:** Port conflict or permission issue.

**Solution:**
1. Open plugin settings
2. Change bridge port to a different value (e.g., 7766)
3. Restart Obsidian
4. Update your MCP client config to use the new port

#### Bridge tools return "[error] [UNAUTHORIZED] ..."

**Cause:** The plugin's "Auth token" setting is configured but the server's `KIOKU_BRIDGE_TOKEN`
is missing, empty, or doesn't match — or vice versa.

**Solution:**
1. Open the Kioku plugin settings in Obsidian and copy the "Auth token" value (or click
   "Generate" if none is set and you want to enable auth).
2. Set `KIOKU_BRIDGE_TOKEN` in the server's environment to the exact same value.
3. Restart both the bridge (plugin command "Restart Kioku MCP Bridge") and the MCP server.
4. To disable auth again, clear the "Auth token" field in the plugin and unset
   `KIOKU_BRIDGE_TOKEN` on the server — an empty token on either side falls back to the
   pre-auth, unauthenticated behavior.

### Connection Issues

#### MCP Client Can't Connect

**Symptoms:** "Connection refused" or timeout errors.

**Checklist:**
1. ✅ Server is running
2. ✅ Vault path is correct
3. ✅ Port matches between server and client config
4. ✅ No firewall blocking the connection
5. ✅ Plugin is enabled in Obsidian

**Debug Steps:**
```bash
# Test server directly
echo '{"jsonrpc":"2.0","id":1,"method":"tools/list"}' | kioku

# Test HTTP endpoint
curl http://localhost:5173/health

# Check plugin console
# Open DevTools in Obsidian (Ctrl+Shift+I) and look for errors
```

### Semantic Search Issues

#### "Ollama not reachable" Warning

**Cause:** Ollama service is not running or not accessible.

**Solution:**
```bash
# Start Ollama
ollama serve

# Verify it's running
curl http://localhost:11434/api/tags

# Pull embedding model
ollama pull nomic-embed-text
```

#### Semantic Search Returns No Results

**Possible Causes:**
1. Embedding model not pulled
2. Vault not indexed yet
3. Index corrupted

**Solutions:**
```bash
# Ensure model is available
ollama list | grep nomic-embed-text

# Force re-index
# Restart the server or call rebuild_index tool

# Check logs for errors
# Look for "Embedding index ready" message
```

### Performance Issues

#### Slow Startup

**Cause:** Large vault with many files to index, or a large embedding backlog (first run,
or after a cache invalidation from an embedding model change).

Keyword indexing itself is fast and never blocked by embeddings — the server reports
`Status: [ok] Ready` and answers `search_notes`/`list_notes`/etc. as soon as the vault's
file index is built. Semantic search (`search_notes(mode='semantic')`) is what's degraded while a
re-embedding backlog is being processed: it silently returns fewer or no semantic results
until the backlog clears (keyword-only tools are unaffected).

Re-embedding runs in the background with limited concurrency (2 requests to Ollama at a
time, to avoid saturating a CPU-only machine) and never blocks the server from starting or
serving keyword search. A note whose content hasn't changed since the last run is never
re-embedded — only new or edited notes enter the backlog.

**Check progress** with `get_server_status`:
```
Embedding backlog: 42
Embedded this session: 158
Embedding rate: 24.3 notes/min
Estimated remaining: 1.7m
```

**Solutions:**
1. Exclude large folders in `.kioku/config.yml`:
   ```yaml
   exclude:
     - .obsidian
     - .trash
     - Attachments
   ```
2. Let the backlog drain in the background (subsequent starts are faster since unchanged
   notes are skipped — see `get_server_status`)
3. Consider splitting into multiple vaults

#### High Memory Usage

**Cause:** Large vault with many embeddings.

**Solutions:**
1. Reduce `KIOKU_MAX_RESULTS` (default: 20)
2. Exclude unnecessary folders
3. Restart server periodically to clear cache

### File Operation Errors

#### "Path escapes the vault" Error

**Cause:** Attempting to access files outside the vault (security feature).

**Solution:**
- This is intentional security protection
- Ensure all file paths are within the vault
- Don't use absolute paths or `../` in tool calls

#### "Note not found" Error

**Possible Causes:**
1. Note doesn't exist
2. Note path is incorrect
3. Index is outdated

**Solutions:**
```bash
# Verify note exists
ls /path/to/vault/note.md

# Rebuild index
# Call rebuild_index tool or restart server

# Use exact path or note name
# Try: "Projects/My Note" instead of just "My Note"
```

## Docker-Specific Issues

### Container Won't Start

```bash
# Check logs
docker logs kioku-server

# Common issues:
# 1. Vault path not mounted correctly
# 2. Port already in use
# 3. Ollama not ready
```

### Ollama Connection Failed

```bash
# Ensure Ollama container is running
docker ps | grep ollama

# Check network connectivity
docker exec kioku-server curl http://ollama:11434/api/tags

# Restart Ollama container
docker restart kioku-ollama
```

## Getting Help

### Logs

**Server logs:**
```bash
# stdio transport - check your MCP client logs
# HTTP transport
docker logs kioku-server
# Or check systemd journal if running as service
journalctl -u kioku-server
```

**Plugin logs:**
- Open Obsidian Developer Console (Ctrl+Shift+I)
- Filter by "[Kioku]"

### Diagnostic Information

Collect this information when reporting issues:

1. **Server version:** `kioku --version` or check release tag
2. **Plugin version:** Check in Obsidian Community Plugins settings
3. **OS:** `uname -a` or Windows version
4. **Node.js version:** `node --version` (if building from source)
5. **.NET version:** `dotnet --version`
6. **Ollama version:** `ollama --version`
7. **Error messages:** Full error text from logs
8. **Configuration:** Environment variables (redact sensitive values)

### Report Issues

- **GitHub Issues:** https://github.com/sandovaldavid/kioku/issues
- **Security Issues:** See [SECURITY.md](../SECURITY.md)

Include:
- Steps to reproduce
- Expected behavior
- Actual behavior
- Logs and diagnostic info
- Your configuration (redact secrets)
