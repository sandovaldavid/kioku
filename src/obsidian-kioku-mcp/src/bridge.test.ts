import { describe, it, expect, vi } from "vitest";
import WebSocket from "ws";
import { BridgeServer } from "./bridge";
import type { BridgeResponse, CommandHandler } from "./types";

function getPort(server: BridgeServer): number {
  return (server as unknown as { wss: { address: () => { port: number } } }).wss.address().port;
}

function waitFor(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

/** Minimal handler map exercising BridgeServer's own auth-gating logic, independent of the
 * real cmdAuth business logic (covered separately in handlers.test.ts). */
function createAuthHandlers(expectedToken?: string): Record<string, CommandHandler> {
  return {
    auth: (payload) => {
      if (!expectedToken) {
        return { success: true, data: { authenticated: true } };
      }
      const token = payload?.token as string | undefined;
      if (token === expectedToken) {
        return { success: true, data: { authenticated: true } };
      }
      return { success: false, error: "[error] [UNAUTHORIZED] Invalid token." };
    },
    ping: () => ({ success: true, data: { pong: true } }),
  };
}

async function connect(port: number): Promise<WebSocket> {
  const client = new WebSocket(`ws://127.0.0.1:${port}`);
  await new Promise<void>((resolve, reject) => {
    client.on("open", resolve);
    client.on("error", reject);
  });
  return client;
}

describe("BridgeServer", () => {
  it("isRunning is false before start and true once started", async () => {
    const server = new BridgeServer(0);
    expect(server.isRunning).toBe(false);

    server.start();
    await waitFor(50);
    expect(server.isRunning).toBe(true);

    server.stop();
  });

  it("isRunning is false after stop", async () => {
    const server = new BridgeServer(0);
    server.start();
    await waitFor(50);

    server.stop();
    expect(server.isRunning).toBe(false);
  });

  it("calls onStateChange when the server starts and stops", async () => {
    const onStateChange = vi.fn();
    const server = new BridgeServer(0, undefined, undefined, undefined, undefined, onStateChange);

    server.start();
    await waitFor(50);
    expect(onStateChange).toHaveBeenCalledTimes(1);

    server.stop();
    expect(onStateChange).toHaveBeenCalledTimes(2);
  });

  it("tracks clientCount and fires onClientConnected/onClientDisconnected", async () => {
    const onClientConnected = vi.fn();
    const onClientDisconnected = vi.fn();
    const server = new BridgeServer(
      0,
      undefined,
      undefined,
      onClientConnected,
      onClientDisconnected
    );
    server.start();
    await waitFor(50);
    expect(server.clientCount).toBe(0);

    const port = getPort(server);
    const client = new WebSocket(`ws://127.0.0.1:${port}`);
    await new Promise<void>((resolve, reject) => {
      client.on("open", resolve);
      client.on("error", reject);
    });

    expect(server.clientCount).toBe(1);
    expect(onClientConnected).toHaveBeenCalledTimes(1);

    client.close();
    await waitFor(50);

    expect(server.clientCount).toBe(0);
    expect(onClientDisconnected).toHaveBeenCalledTimes(1);

    server.stop();
  });

  it("calls onStartupError and reports isRunning=false when the port is already in use", async () => {
    const first = new BridgeServer(0);
    first.start();
    await waitFor(50);
    const port = getPort(first);

    const onStartupError = vi.fn();
    const onStateChange = vi.fn();
    const second = new BridgeServer(
      port,
      onStartupError,
      undefined,
      undefined,
      undefined,
      onStateChange
    );
    second.start();
    await waitFor(100);

    expect(onStartupError).toHaveBeenCalledTimes(1);
    expect(second.isRunning).toBe(false);
    expect(onStateChange).toHaveBeenCalledTimes(1);

    first.stop();
  });

  describe("token auth", () => {
    it("allows commands without authenticating when no token is configured (v1 backward compat)", async () => {
      const server = new BridgeServer(0);
      server.registerHandlers(createAuthHandlers(undefined));
      server.start();
      await waitFor(50);

      const client = await connect(getPort(server));
      const response = await new Promise<BridgeResponse>((resolve) => {
        client.on("message", (data: Buffer) =>
          resolve(JSON.parse(data.toString()) as BridgeResponse)
        );
        client.send(JSON.stringify({ command: "ping", requestId: "req-1" }));
      });

      expect(response.success).toBe(true);
      expect(response.data).toEqual({ pong: true });

      client.close();
      server.stop();
    });

    it("rejects a non-auth command before authenticating when a token is required, and closes the connection", async () => {
      const server = new BridgeServer(
        0,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        "s3cr3t"
      );
      server.registerHandlers(createAuthHandlers("s3cr3t"));
      server.start();
      await waitFor(50);

      const client = await connect(getPort(server));
      const messagePromise = new Promise<BridgeResponse>((resolve) => {
        client.on("message", (data: Buffer) =>
          resolve(JSON.parse(data.toString()) as BridgeResponse)
        );
      });
      const closePromise = new Promise<number>((resolve) => {
        client.on("close", (code) => resolve(code));
      });

      client.send(JSON.stringify({ command: "ping", requestId: "req-1" }));

      const response = await messagePromise;
      expect(response.success).toBe(false);
      expect(response.error).toContain("[UNAUTHORIZED]");

      expect(await closePromise).toBe(4401);

      server.stop();
    });

    it("allows commands after authenticating with the correct token", async () => {
      const server = new BridgeServer(
        0,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        "s3cr3t"
      );
      server.registerHandlers(createAuthHandlers("s3cr3t"));
      server.start();
      await waitFor(50);

      const client = await connect(getPort(server));
      const responses: BridgeResponse[] = [];
      client.on("message", (data: Buffer) =>
        responses.push(JSON.parse(data.toString()) as BridgeResponse)
      );

      client.send(
        JSON.stringify({ command: "auth", payload: { token: "s3cr3t" }, requestId: "auth-1" })
      );
      await waitFor(50);
      expect(responses[0].success).toBe(true);

      client.send(JSON.stringify({ command: "ping", requestId: "ping-1" }));
      await waitFor(50);
      expect(responses[1].success).toBe(true);
      expect(responses[1].data).toEqual({ pong: true });

      client.close();
      server.stop();
    });

    it("rejects auth with the wrong token and closes the connection", async () => {
      const server = new BridgeServer(
        0,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        "s3cr3t"
      );
      server.registerHandlers(createAuthHandlers("s3cr3t"));
      server.start();
      await waitFor(50);

      const client = await connect(getPort(server));
      const messagePromise = new Promise<BridgeResponse>((resolve) => {
        client.on("message", (data: Buffer) =>
          resolve(JSON.parse(data.toString()) as BridgeResponse)
        );
      });
      const closePromise = new Promise<number>((resolve) => {
        client.on("close", (code) => resolve(code));
      });

      client.send(
        JSON.stringify({ command: "auth", payload: { token: "wrong" }, requestId: "auth-1" })
      );

      const response = await messagePromise;
      expect(response.success).toBe(false);

      expect(await closePromise).toBe(4401);

      server.stop();
    });

    it("re-authenticates independently on a fresh connection after a previous one is rejected", async () => {
      const server = new BridgeServer(
        0,
        undefined,
        undefined,
        undefined,
        undefined,
        undefined,
        "s3cr3t"
      );
      server.registerHandlers(createAuthHandlers("s3cr3t"));
      server.start();
      await waitFor(50);
      const port = getPort(server);

      const rejected = await connect(port);
      const rejectedClose = new Promise<number>((resolve) => {
        rejected.on("close", (code) => resolve(code));
      });
      rejected.send(
        JSON.stringify({ command: "auth", payload: { token: "wrong" }, requestId: "auth-1" })
      );
      expect(await rejectedClose).toBe(4401);

      const accepted = await connect(port);
      const response = await new Promise<BridgeResponse>((resolve) => {
        accepted.on("message", (data: Buffer) =>
          resolve(JSON.parse(data.toString()) as BridgeResponse)
        );
        accepted.send(
          JSON.stringify({ command: "auth", payload: { token: "s3cr3t" }, requestId: "auth-2" })
        );
      });

      expect(response.success).toBe(true);

      accepted.close();
      server.stop();
    });
  });
});
