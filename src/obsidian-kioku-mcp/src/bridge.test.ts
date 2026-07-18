import { describe, expect, it, vi } from "vitest";
import WebSocket, { type WebSocketServer } from "ws";
import {
  BRIDGE_HOST,
  HEARTBEAT_INTERVAL_MS,
  MAX_BRIDGE_CLIENTS,
  MAX_MESSAGE_BYTES,
  RATE_LIMIT_REQUESTS,
  REQUEST_TIMEOUT_MS,
  BridgeServer,
} from "./bridge";
import type { BridgeMessage, BridgeResponse, CommandHandler, KiokuSettings } from "./types";
import { DEFAULT_SETTINGS, PROTOCOL_VERSION } from "./types";

function internalServer(server: BridgeServer): WebSocketServer {
  return (server as unknown as { wss: WebSocketServer }).wss;
}

async function start(server: BridgeServer): Promise<number> {
  expect(server.start()).toBe(true);
  const wss = internalServer(server);
  if (!wss.address()) {
    await new Promise<void>((resolve, reject) => {
      wss.once("listening", resolve);
      wss.once("error", reject);
    });
  }
  const address = wss.address();
  if (!address || typeof address === "string") throw new Error("Bridge has no TCP address.");
  return address.port;
}

async function connect(port: number): Promise<WebSocket> {
  const client = new WebSocket(`ws://${BRIDGE_HOST}:${port}`);
  await new Promise<void>((resolve, reject) => {
    client.once("open", resolve);
    client.once("error", reject);
  });
  return client;
}

function nextResponse(client: WebSocket): Promise<BridgeResponse> {
  return new Promise((resolve) => {
    client.once("message", (data: Buffer) => {
      resolve(JSON.parse(data.toString()) as BridgeResponse);
    });
  });
}

async function send(
  client: WebSocket,
  message: BridgeMessage | Record<string, unknown>
): Promise<BridgeResponse> {
  const response = nextResponse(client);
  client.send(JSON.stringify(message));
  return response;
}

async function handshake(
  client: WebSocket,
  options: { token?: string; minimum?: number; maximum?: number; requestId?: string } = {}
): Promise<BridgeResponse> {
  return send(client, {
    command: "auth",
    payload: {
      token: options.token,
      minProtocolVersion: options.minimum ?? PROTOCOL_VERSION,
      maxProtocolVersion: options.maximum ?? PROTOCOL_VERSION,
      clientName: "bridge-test",
      requestedCapabilities: ["read", "ui-navigation", "editor-mutation"],
    },
    requestId: options.requestId ?? "auth-1",
    protocolVersion: PROTOCOL_VERSION,
  });
}

function command(
  name: BridgeMessage["command"],
  requestId: string,
  payload?: Record<string, unknown>,
  protocolVersion = PROTOCOL_VERSION
): Record<string, unknown> {
  return { command: name, payload, requestId, protocolVersion };
}

function handlers(overrides: Record<string, CommandHandler> = {}): Record<string, CommandHandler> {
  return {
    "is-obsidian-ready": () => ({ success: true, data: { ready: true } }),
    "get-active-note": () => ({ success: true, data: null }),
    "trigger-command": () => ({ success: true, data: { executed: true } }),
    ...overrides,
  };
}

function serverWith(
  options: { token?: string; settings?: KiokuSettings; handlerMap?: Record<string, CommandHandler> } = {}
): BridgeServer {
  const server = new BridgeServer(
    0,
    undefined,
    undefined,
    undefined,
    undefined,
    undefined,
    options.token,
    options.settings ?? DEFAULT_SETTINGS
  );
  server.registerHandlers(options.handlerMap ?? handlers());
  return server;
}

describe("BridgeServer hardening", () => {
  it("binds only to loopback and start is idempotent", async () => {
    const server = serverWith();
    await start(server);

    expect(internalServer(server).options.host).toBe(BRIDGE_HOST);
    expect(server.start()).toBe(false);
    expect(server.isRunning).toBe(true);

    await server.stop();
    await server.stop();
    expect(server.isRunning).toBe(false);
  });

  it("warns prominently when no auth token is configured", async () => {
    const warning = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const server = serverWith();
    await start(server);

    expect(warning).toHaveBeenCalledWith(expect.stringContaining("without an auth token"));

    await server.stop();
    warning.mockRestore();
  });

  it("requires a handshake even when open/no-token mode is used", async () => {
    const server = serverWith();
    const client = await connect(await start(server));
    const close = new Promise<number>((resolve) => client.once("close", (code) => resolve(code)));

    const response = await send(client, command("is-obsidian-ready", "req-1"));
    expect(response.errorCode).toBe("HANDSHAKE_REQUIRED");
    expect(await close).toBe(4401);

    await server.stop();
  });

  it("negotiates protocol and capabilities during auth", async () => {
    const server = serverWith();
    const client = await connect(await start(server));

    const response = await handshake(client);
    expect(response.success).toBe(true);
    expect(response.protocolVersion).toBe(PROTOCOL_VERSION);
    expect(response.data).toMatchObject({
      negotiatedProtocolVersion: PROTOCOL_VERSION,
      minProtocolVersion: PROTOCOL_VERSION,
      maxProtocolVersion: PROTOCOL_VERSION,
      capabilities: ["read", "ui-navigation", "editor-mutation"],
      authenticationRequired: false,
    });

    client.close();
    await server.stop();
  });

  it("rejects unsupported protocol ranges and closes deterministically", async () => {
    const mismatch = vi.fn();
    const server = new BridgeServer(0, undefined, mismatch);
    server.registerHandlers(handlers());
    const client = await connect(await start(server));
    const close = new Promise<number>((resolve) => client.once("close", (code) => resolve(code)));

    const response = await handshake(client, { minimum: 99, maximum: 100 });
    expect(response.errorCode).toBe("UNSUPPORTED_PROTOCOL");
    expect(mismatch).toHaveBeenCalled();
    expect(await close).toBe(4406);

    await server.stop();
  });

  it("uses per-connection constant-time token authentication", async () => {
    const server = serverWith({ token: "s3cr3t" });
    const port = await start(server);

    const rejected = await connect(port);
    const rejectedClose = new Promise<number>((resolve) =>
      rejected.once("close", (code) => resolve(code))
    );
    const rejectedResponse = await handshake(rejected, { token: "wrong" });
    expect(rejectedResponse.errorCode).toBe("UNAUTHORIZED");
    expect(await rejectedClose).toBe(4401);

    const accepted = await connect(port);
    expect((await handshake(accepted, { token: "s3cr3t", requestId: "auth-2" })).success).toBe(
      true
    );
    expect(
      (await send(accepted, command("is-obsidian-ready", "ready-1"))).success
    ).toBe(true);

    accepted.close();
    await server.stop();
  });

  it("rejects hostile payloads before handlers run", async () => {
    const handler = vi.fn(() => ({ success: true }));
    const server = serverWith({ handlerMap: handlers({ "open-file": handler }) });
    const client = await connect(await start(server));
    await handshake(client);

    const response = await send(
      client,
      command("open-file", "path-1", { path: "../../outside.md" })
    );
    expect(response.errorCode).toBe("INVALID_PAYLOAD");
    expect(handler).not.toHaveBeenCalled();

    client.close();
    await server.stop();
  });

  it("denies arbitrary command IDs and permits the built-in safe allowlist", async () => {
    const handler = vi.fn(() => ({ success: true, data: { executed: true } }));
    const server = serverWith({ handlerMap: handlers({ "trigger-command": handler }) });
    const client = await connect(await start(server));
    await handshake(client);

    const denied = await send(
      client,
      command("trigger-command", "cmd-1", { commandId: "third-party:destroy" })
    );
    expect(denied.errorCode).toBe("COMMAND_DENIED");
    expect(handler).not.toHaveBeenCalled();

    const allowed = await send(
      client,
      command("trigger-command", "cmd-2", { commandId: "app:toggle-left-sidebar" })
    );
    expect(allowed.success).toBe(true);
    expect(handler).toHaveBeenCalledTimes(1);

    client.close();
    await server.stop();
  });

  it("requires explicit unsafe mode and an explicit custom command allowlist", async () => {
    const handler = vi.fn(() => ({ success: true }));
    const settings: KiokuSettings = {
      ...DEFAULT_SETTINGS,
      allowUnsafeCommands: true,
      additionalAllowedCommandIds: ["custom-plugin:run"],
    };
    const server = serverWith({ settings, handlerMap: handlers({ "trigger-command": handler }) });
    const client = await connect(await start(server));
    await handshake(client);

    expect(
      (
        await send(
          client,
          command("trigger-command", "cmd-1", { commandId: "custom-plugin:run" })
        )
      ).success
    ).toBe(true);
    expect(handler).toHaveBeenCalledTimes(1);

    client.close();
    await server.stop();
  });

  it("detects replayed request IDs per connection", async () => {
    const server = serverWith();
    const client = await connect(await start(server));
    await handshake(client);

    expect((await send(client, command("is-obsidian-ready", "same-id"))).success).toBe(true);
    const replay = await send(client, command("is-obsidian-ready", "same-id"));
    expect(replay.errorCode).toBe("REPLAY_DETECTED");

    client.close();
    await server.stop();
  });

  it("rate-limits a client without affecting the server", async () => {
    const server = serverWith();
    const client = await connect(await start(server));
    await handshake(client);

    let last: BridgeResponse | undefined;
    for (let index = 0; index < RATE_LIMIT_REQUESTS; index++) {
      last = await send(client, command("is-obsidian-ready", `rate-${index}`));
    }
    expect(last?.errorCode).toBe("RATE_LIMITED");
    expect(server.isRunning).toBe(true);

    client.close();
    await server.stop();
  });

  it("limits concurrent clients", async () => {
    const server = serverWith();
    const port = await start(server);
    const clients: WebSocket[] = [];
    for (let index = 0; index < MAX_BRIDGE_CLIENTS; index++) {
      clients.push(await connect(port));
    }

    const rejected = await connect(port);
    const closeCode = await new Promise<number>((resolve) =>
      rejected.once("close", (code) => resolve(code))
    );
    expect(closeCode).toBe(4429);
    expect(server.clientCount).toBe(MAX_BRIDGE_CLIENTS);

    for (const client of clients) client.close();
    await server.stop();
  });

  it("rejects oversized messages at the WebSocket layer", async () => {
    const server = serverWith();
    const client = await connect(await start(server));
    const close = new Promise<number>((resolve) => client.once("close", (code) => resolve(code)));

    client.send("x".repeat(MAX_MESSAGE_BYTES + 1));
    expect(await close).toBe(1009);

    await server.stop();
  });

  it("times out handlers without exposing their internals", async () => {
    const never = () => new Promise<BridgeResponse>(() => undefined);
    const server = serverWith({ handlerMap: handlers({ "get-active-note": never }) });
    const client = await connect(await start(server));
    await handshake(client);

    (server as unknown as { stopHeartbeat: () => void }).stopHeartbeat();
    vi.useFakeTimers();
    const responsePromise = send(client, command("get-active-note", "slow-1"));
    await vi.advanceTimersByTimeAsync(REQUEST_TIMEOUT_MS);
    const response = await responsePromise;
    vi.useRealTimers();

    expect(response.errorCode).toBe("REQUEST_TIMEOUT");
    expect(response.error).not.toContain("stack");

    client.close();
    await server.stop();
  });

  it("terminates stale clients during heartbeat cleanup", async () => {
    vi.useFakeTimers();
    const server = serverWith();
    server.start();
    const wss = internalServer(server);
    await new Promise<void>((resolve, reject) => {
      wss.once("listening", resolve);
      wss.once("error", reject);
    });
    const address = wss.address();
    if (!address || typeof address === "string") throw new Error("Bridge has no TCP address.");

    const client = await connect(address.port);
    const states = (server as unknown as { clients: Map<WebSocket, { isAlive: boolean }> }).clients;
    states.get([...states.keys()][0])!.isAlive = false;
    const closed = new Promise<void>((resolve) => client.once("close", () => resolve()));

    await vi.advanceTimersByTimeAsync(HEARTBEAT_INTERVAL_MS);
    await closed;
    expect(server.clientCount).toBe(0);

    vi.useRealTimers();
    await server.stop();
  });
});
