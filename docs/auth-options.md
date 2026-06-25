# Autenticación para Kioku en VM — Opciones y Recomendaciones

> **Contexto:** Kioku v2 añade transporte HTTP-SSE para exponer el servidor MCP a múltiples agentes.
> Si el servidor corre en una VM accesible desde internet, necesita protección.
> Este documento evalúa las opciones de menor a mayor complejidad y recomienda la arquitectura óptima.

---

## Feedback sobre la idea de subir a una VM

Es una idea sólida con ventajas reales, pero con implicaciones que conviene tener claras:

**Ventajas:**
- El servidor corre 24/7 sin depender de que tu laptop esté encendida.
- Múltiples agentes (Claude Code en laptop + móvil + CI) pueden conectarse simultáneamente.
- El vault puede vivir en la VM y sincronizarse vía Syncthing o Obsidian Sync.

**Riesgos y consideraciones:**
- **El vault contiene notas personales/profesionales** — exposición en internet es un riesgo real.
- Ollama necesita al menos 8GB RAM y GPU dedicada para ser útil en VM; la mayoría de VMs cloud son CPU-only (embeddings serán lentos: ~2-5s/nota vs ~60ms local con GPU).
- Costo: una VM con 8GB RAM + GPU en AWS/GCP ronda los $200-400/mes. Una VM CPU de 4GB RAM es suficiente si usas Ollama sin GPU (~$20/mes en Hetzner o DigitalOcean).
- **Recomendación de infraestructura:** Hetzner CX32 (4 vCPU, 8GB RAM, €13/mes) o Fly.io (free tier limitado). Para GPU: Lambda Labs es el más barato (~$0.50/h bajo demanda).

---

## Opciones de Autenticación

### Opción 1 — Tailscale (RECOMENDADA para uso personal)

**Qué es:** VPN mesh zero-trust. Instalas el cliente en la VM y en tu laptop/PC. El servidor escucha solo en la IP de Tailscale (no en internet público). Sin puertos abiertos al mundo.

**Cómo funciona:**
```
Tu laptop (Tailscale) ──── Tailscale mesh (cifrado) ──── VM (Tailscale)
                                                            │
                                                     Kioku HTTP :5173
                                                     (solo escucha en 100.x.x.x)
```

**Implementación (cero cambios en el servidor):**
```bash
# En la VM
curl -fsSL https://tailscale.com/install.sh | sh
sudo tailscale up

# Arrancar el servidor solo en la IP de Tailscale
KIOKU_VAULT_PATH=/vault dotnet run ... --urls "http://$(tailscale ip -4):5173"
```

**Configuración del cliente MCP:**
```json
{
  "servers": {
    "kioku": {
      "type": "http",
      "url": "http://100.x.x.x:5173/mcp"
    }
  }
}
```

**Pros:**
- Cero cambios de código
- Cifrado end-to-end (WireGuard)
- Free tier: hasta 100 dispositivos
- Funciona desde cualquier red (móvil, trabajo, casa)
- El servidor nunca es accesible desde internet público

**Contras:**
- Requiere instalar Tailscale en cada dispositivo cliente
- Dependencia de los servidores de coordinación de Tailscale (pueden caer, aunque la VPN sigue funcionando)

---

### Opción 2 — Bearer Token / API Key (RECOMENDADA si necesitas acceso HTTP directo)

**Qué es:** El servidor valida un header `Authorization: Bearer <token>` en cada request. El token es una cadena aleatoria configurada por env var.

**Implementación en `Program.cs`:**

```csharp
// Middleware de autenticación por API key
app.Use(async (context, next) =>
{
    var apiKey = config.ApiKey;
    if (string.IsNullOrEmpty(apiKey))
    {
        await next(context); // Sin clave configurada: sin protección (solo localhost)
        return;
    }

    // Excluir health check
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

**Añadir a `KiokuConfiguration.cs`:**
```csharp
// Env var: KIOKU_API_KEY (vacío = sin autenticación, solo para localhost)
public string? ApiKey { get; init; }
```

**Uso desde Claude Code:**
```json
{
  "servers": {
    "kioku": {
      "type": "http",
      "url": "http://vm-ip:5173/mcp",
      "headers": {
        "Authorization": "Bearer tu-token-aqui-32chars-aleatorio"
      }
    }
  }
}
```

**Generar un token seguro:**
```bash
openssl rand -hex 32
# → a3f8c2d1e4b5a6f7c8d9e0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1
```

**Pros:**
- Simple de implementar (~30 líneas)
- Sin dependencias externas
- Funciona con cualquier cliente HTTP
- Combinable con HTTPS (nginx/Caddy)

**Contras:**
- Token no expira (rotación manual)
- Si se filtra el token, hay que rotarlo manualmente
- Requiere HTTPS para ser seguro (sin HTTPS, el token viaja en claro)

---

### Opción 3 — Cloudflare Tunnel + Cloudflare Access

**Qué es:** CF Tunnel crea un túnel saliente desde la VM hacia la edge de Cloudflare. CF Access actúa como proxy de autenticación (email OTP, GitHub OAuth, Google OAuth). Sin puertos abiertos en la VM.

**Cómo funciona:**
```
Claude Code ──── https://kioku.tu-dominio.com ──── Cloudflare Access (auth)
                                                         │
                                              CF Tunnel (outbound)
                                                         │
                                                    VM (sin puertos abiertos)
                                                         │
                                               Kioku HTTP :5173 (solo localhost)
```

**Implementación:**
```bash
# En la VM
curl -L https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-linux-amd64 -o cloudflared
chmod +x cloudflared
./cloudflared tunnel login
./cloudflared tunnel create kioku
./cloudflared tunnel route dns kioku kioku.tu-dominio.com

# config.yml
tunnel: <tunnel-id>
credentials-file: /root/.cloudflared/<tunnel-id>.json
ingress:
  - hostname: kioku.tu-dominio.com
    service: http://localhost:5173
  - service: http_status:404
```

**Pros:**
- Sin puertos abiertos en la VM (solo outbound desde la VM)
- HTTPS automático
- Auth delegada a CF Access (no hay que implementarla)
- Logs y rate limiting gratis en CF

**Contras:**
- Requiere dominio propio (~$10/año)
- Dependencia de Cloudflare
- CF Access tiene límite de 50 users en free tier
- Latencia adicional (todo pasa por CF edge)
- Configuración más compleja

---

### Opción 4 — nginx + HTTPS + Basic Auth (descartada para este caso)

Basic Auth con HTTPS funciona pero es la peor opción aquí: credenciales estáticas, no granular, MCP SDK puede no manejar bien el challenge 401.

---

## Recomendación Final

| Escenario | Recomendación |
|-----------|--------------|
| Uso personal, pocos dispositivos | **Tailscale** — cero código, máxima seguridad |
| Acceso programático desde CI/scripts | **Bearer Token + nginx HTTPS** |
| Quieres URL pública con auth robusta | **Cloudflare Tunnel + Access** |
| Combinación óptima | **Tailscale + Bearer Token** (red privada + token como segunda capa) |

La combinación **Tailscale + Bearer Token** es la más robusta para uso personal: Tailscale garantiza que solo tus dispositivos alcanzan el servidor, y el Bearer Token protege contra accesos accidentales desde la red Tailscale de otros usuarios.

---

## Arquitectura recomendada en la VM

```
internet
    │
    └── BLOQUEADO por firewall (solo puerto 22 SSH)

tailscale (100.x.x.x)
    │
    └── nginx (:443, HTTPS con Let's Encrypt o self-signed)
              │
              └── Kioku HTTP (:5173, localhost only)
                       │
                       ├── VaultIndexService (reads /vault)
                       ├── EmbeddingService (→ Ollama :11434)
                       └── ObsidianBridgeService (→ plugin :7765 si Obsidian en VM)
```

**systemd service para el servidor:**

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
Environment=KIOKU_API_KEY=<tu-token>
Environment=KIOKU_OLLAMA_URL=http://localhost:11434
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

**Sync del vault con Syncthing** (si no usas Obsidian Sync):
```bash
# En la VM
flatpak install flathub me.kozec.syncthingtk
# Compartir carpeta /vault/cortex entre VM y laptop
```

---

## Implementación del Bearer Token en Kioku

Ver commits pendientes en `feat/v2-http-sse`.

Archivos a modificar:
- `KiokuConfiguration.cs`: añadir `ApiKey`
- `Program.cs`: añadir middleware de auth antes de `app.MapMcp()`
- `CLAUDE.md`: añadir `KIOKU_API_KEY` a la tabla de env vars
