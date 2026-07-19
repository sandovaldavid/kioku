import { timingSafeEqual } from "node:crypto";
import WebSocket, { WebSocketServer, type RawData } from "ws";
import { authorizeCommand, getBridgeCapabilities } from "./command-policy";
import { log } from "./logger";
import { MAX_MESSAGE_BYTES, bridgeError, parseBridgeMessage } from "./protocol";
import type {
  AuthPayload,
  BridgeMessage,
  BridgeResponse,
  CommandHandler,
  HandshakeData,
  KiokuSettings,
  RuntimeCommand,
} from "./types";
import {
  DEFAULT_SETTINGS,
  PROTOCOL_MAX_VERSION,
  PROTOCOL_MIN_VERSION,
  PROTOCOL_VERSION,
} from "./types";

export const BRIDGE_HOST = "127.0.0.1";
export const MAX_BRIDGE_CLIENTS = 4;
export const REQUEST_TIMEOUT_MS = 10_000;
export const HEARTBEAT_INTERVAL_MS = 15_000;
export const RATE_LIMIT_WINDOW_MS = 10_000;
export const RATE_LIMIT_REQUESTS = 30;
export const MAX_CONCURRENT_REQUESTS = 4;
export const MAX_BUFFERED_BYTES = 512 * 1024;
export const MAX_REPLAY_IDS = 256;

interface ClientState {
  handshaken: boolean;
  negotiatedProtocolVersion?: number;
  isAlive: boolean;
  requestTimestamps: number[];
  inFlight: number;
  recentRequestIds: string[];
  recentRequestIdSet: Set<string>;
}

export class BridgeServer {
  private wss: WebSocketServer | null = null;
  private readonly clients = new Map<WebSocket, ClientState>();
  private handlers: Record<string, CommandHandler> = {};
  private heartbeatTimer: ReturnType<typeof setInterval> | null = null;
  private stopping: Promise<void> | null = null;

  constructor(
    private readonly port: number,
    private readonly onStartupError?: (message: string) => void,
    private readonly onProtocolMismatch?: (serverVersion: number, clientVersion: number) => void,
    private readonly onClientConnected?: () => void,
    private readonly onClientDisconnected?: () => void,
    private readonly onStateChange?: () => void,
    private readonly authToken?: string,
    private readonly settings: KiokuSettings = DEFAULT_SETTINGS
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

  registerHandlers(handlers: Record<string, CommandHandler>): void {
    this.handlers = handlers;
  }

  start(): boolean {
    if (this.wss) {
      log.warn("Bridge start ignored because the server is already running.");
      return false;
    }

    try {
      const server = new WebSocketServer({
        host: BRIDGE_HOST,
        port: this.port,
        maxPayload: MAX_MESSAGE_BYTES,
        perMessageDeflate: false,
        clientTracking: false,
      });
      this.wss = server;

      if (!this.requiresAuth) {
        log.warn(
          "Bridge is running without an auth token. Loopback binding does not protect against other local processes."
        );
      }

      server.on("connection", (ws) => this.acceptClient(ws));
      server.on("listening", () => {
        log.info(`Bridge listening on ${BRIDGE_HOST}:${this.port}`);
        this.startHeartbeat();
        this.onStateChange?.();
      });
      server.on("error", (error) => {
        log.error(`Could not start the bridge: ${error.message}`);
        if (this.wss === server) {
          this.wss = null;
        }
        this.stopHeartbeat();
        this.onStartupError?.(error.message);
        this.onStateChange?.();
      });

      return true;
    } catch (error) {
      const message = error instanceof Error ? error.message : "Unknown bridge startup error.";
      log.error(`Error starting bridge: ${message}`);
      this.wss = null;
      this.onStartupError?.(message);
      this.onStateChange?.();
      return false;
    }
  }

  async stop(): Promise<void> {
    if (this.stopping) {
      return this.stopping;
    }

    const server = this.wss;
    this.wss = null;
    this.stopHeartbeat();

    for (const client of this.clients.keys()) {
      client.terminate();
    }
    this.clients.clear();
    this.onStateChange?.();

    if (!server) {
      return;
    }

    this.stopping = new Promise<void>((resolve) => {
      server.close(() => resolve());
    }).finally(() => {
      this.stopping = null;
    });

    return this.stopping;
  }

  private acceptClient(ws: WebSocket): void {
    if (this.clients.size >= MAX_BRIDGE_CLIENTS) {
      this.sendResponse(ws, bridgeError("CLIENT_LIMIT", "The bridge client limit was reached."));
      ws.close(4429, "Client limit");
      return;
    }

    const state: ClientState = {
      handshaken: false,
      isAlive: true,
      requestTimestamps: [],
      inFlight: 0,
      recentRequestIds: [],
      recentRequestIdSet: new Set<string>(),
    };
    this.clients.set(ws, state);

    log.info(`Kioku MCP client connected. Clients: ${this.clients.size}`);
    this.onClientConnected?.();

    ws.on("pong", () => {
      state.isAlive = true;
    });
    ws.on("message", (data, isBinary) => {
      void this.handleMessage(ws, state, data, isBinary);
    });
    ws.on("close", () => this.removeClient(ws));
    ws.on("error", (error) => {
      log.error(`WebSocket client error: ${error.message}`);
    });
  }

  private removeClient(ws: WebSocket): void {
    if (!this.clients.delete(ws)) {
      return;
    }
    log.info(`Client disconnected. Clients: ${this.clients.size}`);
    this.onClientDisconnected?.();
  }

  private async handleMessage(
    ws: WebSocket,
    state: ClientState,
    data: RawData,
    isBinary: boolean
  ): Promise<void> {
    if (isBinary) {
      this.sendResponse(
        ws,
        bridgeError("INVALID_MESSAGE", "Binary bridge messages are not supported.")
      );
      ws.close(1003, "Text messages required");
      return;
    }

    if (ws.bufferedAmount > MAX_BUFFERED_BYTES) {
      this.sendResponse(
        ws,
        bridgeError("BACKPRESSURE", "The bridge client is not consuming responses.")
      );
      ws.close(1013, "Backpressure");
      return;
    }

    if (!this.consumeRateLimit(state)) {
      this.sendResponse(ws, bridgeError("RATE_LIMITED", "Too many bridge requests."));
      return;
    }

    let parsedJson: unknown;
    try {
      parsedJson = JSON.parse(this.rawDataToString(data));
    } catch {
      this.sendResponse(ws, bridgeError("INVALID_MESSAGE", "Bridge message is not valid JSON."));
      return;
    }

    const validation = parseBridgeMessage(parsedJson);
    if (!validation.ok) {
      this.sendResponse(
        ws,
        bridgeError(validation.error.code, validation.error.message, validation.error.requestId)
      );
      return;
    }

    const message = validation.message;
    if (!this.rememberRequestId(state, message.requestId)) {
      this.sendResponse(
        ws,
        bridgeError(
          "REPLAY_DETECTED",
          "The requestId was already used on this connection.",
          message.requestId
        )
      );
      return;
    }

    if (message.command === "auth") {
      this.handleHandshake(ws, state, message);
      return;
    }

    if (!state.handshaken || state.negotiatedProtocolVersion === undefined) {
      this.sendResponse(
        ws,
        bridgeError(
          "HANDSHAKE_REQUIRED",
          "Authenticate and negotiate bridge capabilities before sending commands.",
          message.requestId
        )
      );
      ws.close(4401, "Handshake required");
      return;
    }

    if (message.protocolVersion !== state.negotiatedProtocolVersion) {
      this.onProtocolMismatch?.(state.negotiatedProtocolVersion, message.protocolVersion);
      this.sendResponse(
        ws,
        bridgeError(
          "UNSUPPORTED_PROTOCOL",
          "The request protocol version does not match the negotiated version.",
          message.requestId,
          state.negotiatedProtocolVersion
        )
      );
      ws.close(4406, "Protocol mismatch");
      return;
    }

    const authorization = authorizeCommand(
      message as Extract<BridgeMessage, { command: RuntimeCommand }>,
      this.settings
    );
    if (!authorization.allowed) {
      this.sendResponse(
        ws,
        bridgeError(
          authorization.code ?? "COMMAND_DENIED",
          authorization.message ?? "The command is not authorized.",
          message.requestId,
          state.negotiatedProtocolVersion
        )
      );
      return;
    }

    if (state.inFlight >= MAX_CONCURRENT_REQUESTS) {
      this.sendResponse(
        ws,
        bridgeError(
          "RATE_LIMITED",
          "Too many concurrent bridge requests.",
          message.requestId,
          state.negotiatedProtocolVersion
        )
      );
      return;
    }

    state.inFlight++;
    try {
      const response = await this.dispatchWithTimeout(message, state.negotiatedProtocolVersion);
      this.sendResponse(ws, response);
    } finally {
      state.inFlight--;
    }
  }

  private handleHandshake(
    ws: WebSocket,
    state: ClientState,
    message: Extract<BridgeMessage, { command: "auth" }>
  ): void {
    if (state.handshaken) {
      this.sendResponse(
        ws,
        bridgeError(
          "HANDSHAKE_ALREADY_COMPLETED",
          "The bridge handshake has already completed on this connection.",
          message.requestId,
          state.negotiatedProtocolVersion ?? PROTOCOL_VERSION
        )
      );
      return;
    }

    const payload = message.payload as AuthPayload;
    const negotiatedVersion = Math.min(payload.maxProtocolVersion, PROTOCOL_MAX_VERSION);
    const minimumAccepted = Math.max(payload.minProtocolVersion, PROTOCOL_MIN_VERSION);
    if (minimumAccepted > negotiatedVersion) {
      this.onProtocolMismatch?.(PROTOCOL_VERSION, payload.maxProtocolVersion);
      this.sendResponse(
        ws,
        bridgeError(
          "UNSUPPORTED_PROTOCOL",
          `Supported bridge protocol range is ${PROTOCOL_MIN_VERSION}-${PROTOCOL_MAX_VERSION}.`,
          message.requestId
        )
      );
      ws.close(4406, "Unsupported protocol");
      return;
    }

    if (this.requiresAuth && !this.isTokenValid(payload.token)) {
      this.sendResponse(
        ws,
        bridgeError(
          "UNAUTHORIZED",
          "Invalid or missing authentication token.",
          message.requestId,
          negotiatedVersion
        )
      );
      ws.close(4401, "Unauthorized");
      return;
    }

    const availableCapabilities = getBridgeCapabilities(this.settings);
    const requested = payload.requestedCapabilities;
    const negotiatedCapabilities = requested?.length
      ? availableCapabilities.filter((capability) => requested.includes(capability))
      : availableCapabilities;

    state.handshaken = true;
    state.negotiatedProtocolVersion = negotiatedVersion;

    const data: HandshakeData = {
      negotiatedProtocolVersion: negotiatedVersion,
      minProtocolVersion: PROTOCOL_MIN_VERSION,
      maxProtocolVersion: PROTOCOL_MAX_VERSION,
      capabilities: negotiatedCapabilities,
      authenticationRequired: this.requiresAuth,
    };
    this.sendResponse(ws, {
      requestId: message.requestId,
      success: true,
      data,
      protocolVersion: negotiatedVersion,
    });
  }

  private isTokenValid(actual: string | undefined): boolean {
    if (!this.requiresAuth) {
      return true;
    }
    if (!actual || !this.authToken) {
      return false;
    }

    const expectedBuffer = Buffer.from(this.authToken, "utf8");
    const actualBuffer = Buffer.from(actual, "utf8");
    return (
      expectedBuffer.length === actualBuffer.length && timingSafeEqual(expectedBuffer, actualBuffer)
    );
  }

  private async dispatchWithTimeout(
    message: Extract<BridgeMessage, { command: RuntimeCommand }>,
    protocolVersion: number
  ): Promise<BridgeResponse> {
    const handler = this.handlers[message.command];
    if (!handler) {
      return bridgeError(
        "UNKNOWN_COMMAND",
        "The requested bridge command is not registered.",
        message.requestId,
        protocolVersion
      );
    }

    let timeout: ReturnType<typeof setTimeout> | undefined;
    try {
      const handlerPromise = Promise.resolve(
        handler(message.payload as Record<string, unknown> | undefined, message.requestId)
      );
      const timeoutPromise = new Promise<BridgeResponse>((resolve) => {
        timeout = setTimeout(
          () =>
            resolve(
              bridgeError(
                "REQUEST_TIMEOUT",
                "The bridge command exceeded its execution timeout.",
                message.requestId,
                protocolVersion
              )
            ),
          REQUEST_TIMEOUT_MS
        );
      });

      const result = await Promise.race([handlerPromise, timeoutPromise]);
      if (!result.success) {
        return bridgeError(
          result.errorCode ?? "COMMAND_FAILED",
          "The Obsidian command could not be completed.",
          message.requestId,
          protocolVersion
        );
      }

      return {
        requestId: message.requestId,
        success: true,
        data: result.data,
        protocolVersion,
      };
    } catch (error) {
      const messageText = error instanceof Error ? error.message : "Unknown command failure.";
      log.error(`Bridge command failed: ${messageText}`);
      return bridgeError(
        "INTERNAL_ERROR",
        "The bridge command failed internally.",
        message.requestId,
        protocolVersion
      );
    } finally {
      if (timeout) {
        clearTimeout(timeout);
      }
    }
  }

  private consumeRateLimit(state: ClientState): boolean {
    const now = Date.now();
    state.requestTimestamps = state.requestTimestamps.filter(
      (timestamp) => now - timestamp < RATE_LIMIT_WINDOW_MS
    );
    if (state.requestTimestamps.length >= RATE_LIMIT_REQUESTS) {
      return false;
    }
    state.requestTimestamps.push(now);
    return true;
  }

  private rememberRequestId(state: ClientState, requestId: string): boolean {
    if (state.recentRequestIdSet.has(requestId)) {
      return false;
    }

    state.recentRequestIdSet.add(requestId);
    state.recentRequestIds.push(requestId);
    if (state.recentRequestIds.length > MAX_REPLAY_IDS) {
      const removed = state.recentRequestIds.shift();
      if (removed) {
        state.recentRequestIdSet.delete(removed);
      }
    }
    return true;
  }

  private sendResponse(ws: WebSocket, response: BridgeResponse): void {
    if (ws.readyState !== WebSocket.OPEN) {
      return;
    }
    if (ws.bufferedAmount > MAX_BUFFERED_BYTES) {
      ws.close(1013, "Backpressure");
      return;
    }
    ws.send(JSON.stringify(response));
  }

  private rawDataToString(data: RawData): string {
    if (Buffer.isBuffer(data)) {
      return data.toString("utf8");
    }
    if (Array.isArray(data)) {
      return Buffer.concat(data).toString("utf8");
    }
    return Buffer.from(data).toString("utf8");
  }

  private startHeartbeat(): void {
    this.stopHeartbeat();
    this.heartbeatTimer = setInterval(() => {
      for (const [client, state] of this.clients) {
        if (!state.isAlive) {
          client.terminate();
          this.removeClient(client);
          continue;
        }
        state.isAlive = false;
        client.ping();
      }
    }, HEARTBEAT_INTERVAL_MS);
  }

  private stopHeartbeat(): void {
    if (this.heartbeatTimer) {
      clearInterval(this.heartbeatTimer);
      this.heartbeatTimer = null;
    }
  }
}
