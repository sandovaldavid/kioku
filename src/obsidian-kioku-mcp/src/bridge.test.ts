import { describe, it, expect, vi } from "vitest";
import WebSocket from "ws";
import { BridgeServer } from "./bridge";

function getPort(server: BridgeServer): number {
  return (server as unknown as { wss: { address: () => { port: number } } }).wss.address().port;
}

function waitFor(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
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
});
