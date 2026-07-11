---
layout: default
title: Auth & Security Options
sidebar: true
---

> **Context:** Kioku v2 adds HTTP-SSE transport to expose the MCP server to multiple agents.
> If the server runs on a VM accessible from the internet, it needs protection.
> This document evaluates the options from lowest to highest complexity and recommends the optimal architecture.

---

## Feedback on the idea of deploying to a VM

It's a solid idea with real advantages, but with implications worth being clear about:

**Advantages:**
- The server runs 24/7 without depending on your laptop being on.
- Multiple agents (Claude Code on laptop + mobile + CI) can connect simultaneously.
- The vault can live on the VM and sync via Syncthing or Obsidian Sync.

**Risks and considerations:**
- **The vault contains personal/professional notes** — exposure on the internet is a real risk.
- Ollama needs at least 8GB RAM and a dedicated GPU to be useful on a VM; most cloud VMs are CPU-only (embeddings will be slow: ~2-5s/note vs ~60ms locally with GPU).
- Cost: a VM with 8GB RAM + GPU on AWS/GCP runs around $200-400/month. A 4GB RAM CPU VM is enough if you use Ollama without a GPU (~$20/month on Hetzner or DigitalOcean).
- **Infrastructure recommendation:** Hetzner CX32 (4 vCPU, 8GB RAM, €13/month) or Fly.io (limited free tier). For GPU: Lambda Labs is the cheapest (~$0.50/h on demand).

---

## Authentication Options

### Option 1 — Tailscale (RECOMMENDED for personal use)

**What it is:** A zero-trust mesh VPN. You install the client on the VM and on your laptop/PC. The server listens only on the Tailscale IP (not on the public internet). No ports open to the world.

**How it works:**
```
Your laptop (Tailscale) ──── Tailscale mesh (encrypted) ──── VM (Tailscale)
                                                            │
                                                     Kioku HTTP :5173
                                                     (listens only on 100.x.x.x)
```

**Implementation (zero server changes):**
```bash
# On the VM
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up

# Start the server only on the Tailscale IP
KIOKU_VAULT_PATH=/vault dotnet run ... --urls "http://$(tailscale ip -4):5173"
```

**MCP client configuration** (Claude Code format; in VS Code the root key is `"servers"`):
```json
{
  "mcpServers": {
    "kioku": {
      "type": "sse",
      "url": "http://100.x.x.x:5173/mcp"
    }
  }
}
```

**Pros:**
- Zero code changes
- End-to-end encryption (WireGuard)
- Free tier: up to 100 devices
- Works from any network (mobile, work, home)
- The server is never accessible from the public internet

**Cons:**
- Requires installing Tailscale on every client device
- Depends on Tailscale's coordination servers (they can go down, though the VPN keeps working)

---

### Option 2 — Bearer Token / API Key (RECOMMENDED if you need direct HTTP access)

**What it is:** The server validates an `Authorization: Bearer <token>` header on every request. The token is a random string configured via an env var.

**Implementation** (today lives in `Middleware/ApiKeyMiddleware.cs`; equivalent logic to):

```csharp
// API key authentication middleware
app.Use(async (context, next) =>
{
    var apiKey = config.ApiKey;
    if (string.IsNullOrEmpty(apiKey))
    {
        await next(context); // No key configured: no protection (localhost only)
        return;
    }

    // Exclude health check
    if (context.Request.Path == "/health")
    {
        await next(context);
        return;
    }

    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader)
        || !authHeader.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
        || authHeader.ToString()["Bearer ".Length..].Trim() != apiKey)
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsync("[error] Unauthorized");
        return;
    }

    await next(context);
});
```

**In `KiokuConfiguration.cs`:**
```csharp
// Env var: KIOKU_API_KEY (empty = no authentication, localhost only)
public string? ApiKey { get; init; }
```

**Usage from Claude Code:**
```json
{
  "mcpServers": {
    "kioku": {
      "type": "sse",
      "url": "http://vm-ip:5173/mcp",
      "headers": {
        "Authorization": "Bearer your-random-32char-token-here"
      }
    }
  }
}
```

**Generate a secure token:**
```bash
openssl rand -hex 32
# → a3f8c2d1e4b5a6f7c8d9e0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1
```

**Pros:**
- Simple to implement (~30 lines)
- No external dependencies
- Works with any HTTP client
- Combinable with HTTPS (nginx/Caddy)

**Cons:**
- Token doesn't expire (manual rotation)
- If the token leaks, it must be rotated manually
- Requires HTTPS to be secure (without HTTPS, the token travels in cleartext)

---

### Option 3 — Cloudflare Tunnel + Cloudflare Access

**What it is:** CF Tunnel creates an outbound tunnel from the VM to the Cloudflare edge. CF Access acts as an authentication proxy (email OTP, GitHub OAuth, Google OAuth). No ports open on the VM.

**How it works:**
```
Claude Code ──── https://kioku.your-domain.com ──── Cloudflare Access (auth)
                                                         │
                                              CF Tunnel (outbound)
                                                         │
                                                    VM (no open ports)
                                                         │
                                               Kioku HTTP :5173 (localhost only)
```

**Implementation:**
```bash
# On the VM
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o cloudflared
chmod +x cloudflared
./cloudflared tunnel login
./cloudflared tunnel create kioku
./cloudflared tunnel route dns kioku kioku.your-domain.com

# config.yml
tunnel: <tunnel-id>
credentials-file: /root/.cloudflared/<tunnel-id>.json
ingress:
  - hostname: kioku.your-domain.com
    service: http://localhost:5173
  - service: http_status:404
```

**Pros:**
- No open ports on the VM (only outbound from the VM)
- Automatic HTTPS
- Auth delegated to CF Access (no need to implement it)
- Free logs and rate limiting on CF

**Cons:**
- Requires your own domain (~$10/year)
- Dependency on Cloudflare
- CF Access has a 50-user limit on the free tier
- Extra latency (everything passes through the CF edge)
- More complex configuration

---

### Option 4 — nginx + HTTPS + Basic Auth (discarded for this case)

Basic Auth with HTTPS works but is the worst option here: static credentials, not granular, the MCP SDK may not handle the 401 challenge well.

---

## Final Recommendation

| Scenario | Recommendation |
|-----------|--------------|
| Personal use, few devices | **Tailscale** — zero code, maximum security |
| Programmatic access from CI/scripts | **Bearer Token + nginx HTTPS** |
| You want a public URL with robust auth | **Cloudflare Tunnel + Access** |
| Optimal combination | **Tailscale + Bearer Token** (private network + token as a second layer) |

The **Tailscale + Bearer Token** combination is the most robust for personal use: Tailscale guarantees that only your devices can reach the server, and the Bearer Token protects against accidental access from other users' Tailscale networks.

---

## Recommended VM Architecture

```
internet
    │
    └── BLOCKED by firewall (only port 22 SSH)

tailscale (100.x.x.x)
    │
    └── nginx (:443, HTTPS with Let's Encrypt or self-signed)
              │
              └── Kioku HTTP (:5173, localhost only)
                       │
                       ├── VaultIndexService (reads /vault)
                       ├── EmbeddingService (→ Ollama :11434)
                       └── ObsidianBridgeService (→ plugin :7765 if Obsidian is on the VM)
```

**systemd service for the server:**

```ini
# /etc/systemd/system/kioku.service
[Unit]
Description=Kioku MCP Server
After=network.target ollama.service

[Service]
Type=simple
User=kioku
WorkingDirectory=/opt/kioku
ExecStart=/opt/kioku/Kioku.Mcp.Server
Environment=KIOKU_VAULT_PATH=/vault/cortex
Environment=KIOKU_API_KEY=<your-token>
Environment=KIOKU_OLLAMA_URL=http://localhost:11434
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

**Vault sync with Syncthing** (if you're not using Obsidian Sync):
```bash
# On the VM
flatpak install flathub me.kozec.syncthingtk
# Share the /vault/cortex folder between the VM and laptop
```

---

## Bearer Token Implementation in Kioku

✅ **Implemented** (merged from `feat/v2-http-sse`):

- `KiokuConfiguration.cs` reads `KIOKU_API_KEY`
- `Middleware/ApiKeyMiddleware.cs` validates `Authorization: Bearer <token>` before `app.MapMcp()`
  (`/health` is exempt; with no key configured, access is open — for local development only)
See [ApiKeyMiddleware.cs](../../src/Kioku.Mcp.Server/Middleware/ApiKeyMiddleware.cs) for middleware details.

---

## Hybrid Architecture: Server on VM + Local Ollama and Obsidian

This architecture allows hosting the Kioku MCP server on a cheap VM (no GPU) 24/7, delegating the heavy compute of embeddings to the user's local laptop/PC GPU while keeping interaction with the Obsidian UI.

### Architecture Diagram

```mermaid
graph TD
    subgraph VM_Cloud ["Cloud VM (Cheap CPU)"]
        Server["Kioku MCP Server"]
        VaultDir["Vault Directory (/vault)"]
        Server -->|Reads/Writes| VaultDir
    end

    subgraph Laptop ["Local Laptop / PC (With GPU)"]
        Obsidian["Obsidian + Kioku Plugin (:7765)"]
        Ollama["Ollama (:11434 nomic-embed-text)"]
        LocalVault["Local Vault (.md files)"]
    end

    %% File Synchronization
    LocalVault <-->|Sync: Obsidian Sync / Syncthing| VaultDir

    %% Network Connectivity
    Server -->|Embeddings (HTTP POST)| Ollama
    Server -->|UI Bridge (WebSocket)| Obsidian
```

### Network Connectivity Options

For the Kioku server (on the VM) to communicate with Ollama and Obsidian (on the local machine), there are two main methods:

#### Option A: Reverse SSH Tunnels (Recommended for simplicity and security)
When you connect to the VM via SSH, you can forward ports from your local machine to the VM securely.

* **Command to connect:**
  ```bash
  ssh -R 11434:localhost:11434 -R 7765:localhost:7765 user@vm-ip
  ```
  *(Tip: You can configure this in your local `~/.ssh/config` using `RemoteForward` so it happens automatically when you run `ssh vm`).*

* **How the Server on the VM sees it:**
  * Ollama is available at `http://localhost:11434`
  * The Obsidian Bridge is available at `localhost:7765`

* **Environment Variable Configuration on the VM:**
  ```ini
  KIOKU_OLLAMA_URL=http://localhost:11434
  KIOKU_OBSIDIAN_PORT=7765
  ```

* **Advantages:**
  * No need to configure firewalls or expose ports on your local machine.
  * All traffic travels encrypted within the existing SSH tunnel.
  * Works immediately without depending on external services.

---

#### Option B: Tailscale (Private Mesh Network)
If you'd rather not depend on keeping an SSH session open in the foreground, you can use Tailscale to interconnect the VM and your laptop.

1. **Configure Ollama on the Laptop:**
   By default, Ollama only listens on `127.0.0.1`. You must configure it to listen on all interfaces by setting the environment variable on your local machine:
   ```bash
   OLLAMA_HOST=0.0.0.0
   ```
2. **Environment Variable Configuration on the VM:**
   Point the server to your laptop's Tailscale IP:
   ```ini
   KIOKU_OLLAMA_URL=http://<Laptop-Tailscale-IP>:11434
   KIOKU_OBSIDIAN_PORT=7765 # Obsidian connects via the Tailscale IP
   ```

* **Advantages:**
  * Persistent background connection without requiring an active SSH session.
* **Disadvantages:**
  * Requires exposing Ollama on the local network interface (even though it's protected by Tailscale, care must be taken with local firewall policies).

---

### Vault and Embeddings Database Synchronization

Since the MCP server reads and writes directly to the VM's filesystem, the `.md` files must be synchronized in real time between the VM and the local laptop.

1. **Note Synchronization:**
   * It's recommended to use official **Obsidian Sync** or **Syncthing** to keep the VM's `/vault` directory synchronized with your local machine's.
2. **Excluding Cache Files (`.kioku`):**
   * The binary embeddings cache file `vault/.kioku/embeddings.bin` is managed locally by the server on the VM.
   * **Important:** You must configure **Syncthing** (using `.stignore`) or the sync plugin's exclusion rule to **ignore** the `.kioku/` folder. This prevents sync conflicts and redundant writes to the laptop's local disk.

### Robustness and Fault Tolerance

The Kioku server is designed with graceful degradation:
* **If the laptop is off or the tunnel is closed:**
  * The server on the VM will continue responding to all normal read, write, and semantic search queries (using the previously generated local binary embeddings database `.kioku/embeddings.bin`).
  * Semantic searches for new queries or indexing of new notes will fail gracefully and report that Ollama is unavailable (returning the `[info]` prefix), but the general MCP server will keep operating with normal keyword searches.
  * The Obsidian Bridge tools (e.g. `open_note_in_obsidian`) will report a graceful error indicating that the Obsidian UI is not connected.
