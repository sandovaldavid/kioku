import WebSocket, { WebSocketServer } from "ws";
import { log } from "./logger";
import type { BridgeMessage, BridgeResponse, CommandHandler } from "./types";
import { PROTOCOL_VERSION } from "./types";

export class BridgeServer {
  private wss: WebSocketServer | null = null;
  private clients = new Set<WebSocket>();
  private authenticated = new Set<WebSocket>();
  private handlers: Record<string, CommandHandler> = {};

  constructor(
    private port: number,
    private onStartupError?: (message: string) => void,
    private onProtocolMismatch?: (serverVersion: number, clientVersion: number) => void,
    private onClientConnected?: () => void,
    private onClientDisconnected?: () => void,
    private onStateChange?: () => void,
    private authToken?: string
  ) {}

  get clientCount(): number {
    return this.clients.size;
  }

  get isRunning(): boolean {
    return this.wss !== null;
  }

  private get requiresAuth(): boolean {
    return Boolean(this.authToken && this.authToken.length > 0);
  }

  registerHandlers(handlers: Record<string, CommandHandler>) {
    this.handlers = handlers;
  }

  start() {
    try {
      this.wss = new WebSocketServer({
        host: "127.0.0.1",
        port: this.port,
      });

      this.wss.on("connection", (ws) => {
        this.clients.add(ws);
        log.info(`Kioku MCP Server connected. Clients: ${this.clients.size}`);
        this.onClientConnected?.();

        ws.on("message", async (data) => {
          try {
            const raw = Buffer.isBuffer(data)
              ? data.toString("utf8")
              : Array.isArray(data)
                ? Buffer.concat(data).toString("utf8")
                : Buffer.from(data).toString("utf8");
            const msg = JSON.parse(raw) as BridgeMessage;

            // Check protocol version
            if (msg.protocolVersion && msg.protocolVersion !== PROTOCOL_VERSION) {
              this.onProtocolMismatch?.(PROTOCOL_VERSION, msg.protocolVersion);
            }

            if (this.requiresAuth && msg.command !== "auth" && !this.authenticated.has(ws)) {
              this.rejectUnauthenticated(ws, msg.requestId);
              return;
            }

            const response = await this.dispatch(msg);

            if (msg.command === "auth") {
              if (response.success) {
                this.authenticated.add(ws);
              } else if (this.requiresAuth) {
                ws.send(JSON.stringify(response));
                ws.close(4401, "Unauthorized");
                return;
              }
            }

            ws.send(JSON.stringify(response));
          } catch (err) {
            ws.send(
              JSON.stringify({
                success: false,
                error: String(err),
                protocolVersion: PROTOCOL_VERSION,
              })
            );
          }
        });

        ws.on("close", () => {
          this.clients.delete(ws);
          this.authenticated.delete(ws);
          log.info(`Client disconnected. Clients: ${this.clients.size}`);
          this.onClientDisconnected?.();
        });

        ws.on("error", (err) => {
          log.error(`WebSocket error: ${err.message}`);
        });
      });

      this.wss.on("listening", () => {
        log.info(`Bridge listening on 127.0.0.1:${this.port}`);
        this.onStateChange?.();
      });

      this.wss.on("error", (err) => {
        log.error(`Could not start the bridge: ${err.message}`);
        this.wss = null;
        this.onStartupError?.(err.message);
        this.onStateChange?.();
      });
    } catch (err) {
      log.error("Error starting bridge:", err);
    }
  }

  stop() {
    for (const client of this.clients) {
      client.close();
    }
    this.clients.clear();
    this.authenticated.clear();

    if (this.wss) {
      this.wss.close();
      this.wss = null;
      this.onStateChange?.();
    }
  }

  private rejectUnauthenticated(ws: WebSocket, requestId?: string) {
    const response: BridgeResponse = {
      requestId,
      success: false,
      error: "[error] [UNAUTHORIZED] Authenticate first with the 'auth' command.",
      protocolVersion: PROTOCOL_VERSION,
    };
    ws.send(JSON.stringify(response));
    ws.close(4401, "Unauthorized");
  }

  private async dispatch(msg: BridgeMessage): Promise<BridgeResponse> {
    const { command, payload, requestId } = msg;
    const handler = this.handlers[command];

    if (!handler) {
      return {
        requestId,
        success: false,
        error: `Unknown command: ${command}`,
        protocolVersion: PROTOCOL_VERSION,
      };
    }

    try {
      const result = await handler(payload, requestId);
      return { ...result, requestId, protocolVersion: PROTOCOL_VERSION };
    } catch (err) {
      return { requestId, success: false, error: String(err), protocolVersion: PROTOCOL_VERSION };
    }
  }
}
