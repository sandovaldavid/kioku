import WebSocket, { WebSocketServer } from "ws";
import { log } from "./logger";
import type { BridgeMessage, BridgeResponse, CommandHandler } from "./types";
import { PROTOCOL_VERSION } from "./types";

export class BridgeServer {
  private wss: WebSocketServer | null = null;
  private clients = new Set<WebSocket>();
  private handlers: Record<string, CommandHandler> = {};

  constructor(
    private port: number,
    private onStartupError?: (message: string) => void,
    private onProtocolMismatch?: (serverVersion: number, clientVersion: number) => void
  ) {}

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

            const response = await this.dispatch(msg);
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
          log.info(`Client disconnected. Clients: ${this.clients.size}`);
        });

        ws.on("error", (err) => {
          log.error(`WebSocket error: ${err.message}`);
        });
      });

      this.wss.on("error", (err) => {
        log.error(`Could not start the bridge: ${err.message}`);
        this.onStartupError?.(err.message);
      });

      log.info(`Bridge listening on 127.0.0.1:${this.port}`);
    } catch (err) {
      log.error("Error starting bridge:", err);
    }
  }

  stop() {
    for (const client of this.clients) {
      client.close();
    }
    this.clients.clear();

    if (this.wss) {
      this.wss.close();
      this.wss = null;
    }
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
