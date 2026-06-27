import { describe, it, expect, vi } from "vitest";
import WebSocket from "ws";
import { BridgeServer } from "./bridge";
import { createHandlers } from "./handlers";
import type { BridgeMessage, BridgeResponse } from "./types";
import { PROTOCOL_VERSION, DEFAULT_SETTINGS } from "./types";
import { createMockApp } from "./test-utils";
import type { App } from "obsidian";

vi.mock("obsidian", () => import("./__mocks__/obsidian"));

const manifest = {
  name: "Kioku",
  version: "1.8.0-beta.5",
  author: "David Sandoval",
  description: "Obsidian plugin bridge for Kioku MCP Server",
};

function isValidBridgeMessage(value: unknown): value is BridgeMessage {
  if (typeof value !== "object" || value === null) return false;
  const msg = value as Partial<BridgeMessage>;
  if (typeof msg.command !== "string") return false;
  if (msg.payload !== undefined && (typeof msg.payload !== "object" || msg.payload === null))
    return false;
  if (msg.requestId !== undefined && typeof msg.requestId !== "string") return false;
  if (msg.protocolVersion !== undefined && typeof msg.protocolVersion !== "number") return false;
  return true;
}

function isValidBridgeResponse(value: unknown): value is BridgeResponse {
  if (typeof value !== "object" || value === null) return false;
  const res = value as Partial<BridgeResponse>;
  if (typeof res.success !== "boolean") return false;
  if (res.requestId !== undefined && typeof res.requestId !== "string") return false;
  if (res.error !== undefined && typeof res.error !== "string") return false;
  if (res.protocolVersion !== undefined && typeof res.protocolVersion !== "number") return false;
  // data is intentionally untyped; its presence alone is valid.
  return true;
}

function makeApp() {
  return createMockApp() as unknown as App;
}

describe("Bridge protocol contract", () => {
  describe("message fixtures", () => {
    const fixtures: BridgeMessage[] = [
      { command: "ping" },
      { command: "open-file", payload: { path: "Notes/A.md" }, requestId: "req-1" },
      {
        command: "get-active-note",
        requestId: "req-2",
        protocolVersion: PROTOCOL_VERSION,
      },
      { command: "trigger-command", payload: { commandId: "app:toggle-left-sidebar" } },
    ];

    it.each(fixtures)("validates message fixture %j", (msg) => {
      expect(isValidBridgeMessage(msg)).toBe(true);
    });
  });

  describe("response fixtures", () => {
    const fixtures: BridgeResponse[] = [
      { success: true, data: { ready: true }, requestId: "req-1" },
      { success: false, error: "Missing field", requestId: "req-2" },
      { success: true, data: null, requestId: "req-3", protocolVersion: PROTOCOL_VERSION },
    ];

    it.each(fixtures)("validates response fixture %j", (res) => {
      expect(isValidBridgeResponse(res)).toBe(true);
    });

    it("rejects a response without success", () => {
      expect(isValidBridgeResponse({ data: {} })).toBe(false);
    });

    it("rejects a response with an invalid requestId type", () => {
      expect(isValidBridgeResponse({ success: true, requestId: 123 })).toBe(false);
    });
  });

  describe("handler responses always satisfy the contract", () => {
    const handlers = createHandlers(makeApp(), DEFAULT_SETTINGS, manifest);
    const handlerNames = Object.keys(handlers);

    it.each(handlerNames)("%s returns a valid BridgeResponse shape", async (command) => {
      const handler = handlers[command as keyof typeof handlers];
      const result = await handler({}, "contract-req");
      expect(isValidBridgeResponse(result)).toBe(true);
      // Ensure requestId is echoed when provided.
      expect(result.requestId).toBe("contract-req");
    });
  });

  describe("protocol version consistency", () => {
    it("PROTOCOL_VERSION is a positive integer", () => {
      expect(typeof PROTOCOL_VERSION).toBe("number");
      expect(Number.isInteger(PROTOCOL_VERSION)).toBe(true);
      expect(PROTOCOL_VERSION).toBeGreaterThan(0);
    });

    it("BridgeServer enriches dispatched responses with protocolVersion", async () => {
      const handlers = createHandlers(makeApp(), DEFAULT_SETTINGS, manifest);
      const server = new BridgeServer(0);
      server.registerHandlers(handlers);
      server.start();
      await new Promise<void>((resolve) => setTimeout(resolve, 50));

      const port = (server as unknown as { wss: { address: () => { port: number } } }).wss.address()
        .port;
      const client = new WebSocket(`ws://127.0.0.1:${port}`);

      await new Promise<void>((resolve, reject) => {
        client.on("open", resolve);
        client.on("error", reject);
      });

      const response = await new Promise<BridgeResponse>((resolve) => {
        client.on("message", (data: Buffer) => {
          resolve(JSON.parse(data.toString()) as BridgeResponse);
        });
        client.send(JSON.stringify({ command: "is-obsidian-ready", requestId: "proto-req" }));
      });

      expect(response.success).toBe(true);
      expect(response.protocolVersion).toBe(PROTOCOL_VERSION);
      expect(response.requestId).toBe("proto-req");

      client.close();
      server.stop();
    });
  });
});
