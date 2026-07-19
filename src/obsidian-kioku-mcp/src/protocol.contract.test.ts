import { describe, expect, it, vi } from "vitest";
import { createHandlers } from "./handlers";
import { isSafeVaultPath, parseBridgeMessage } from "./protocol";
import type { BridgeMessage, BridgeResponse } from "./types";
import {
  DEFAULT_SETTINGS,
  PROTOCOL_MAX_VERSION,
  PROTOCOL_MIN_VERSION,
  PROTOCOL_VERSION,
} from "./types";
import { createMockApp } from "./test-utils";
import type { App } from "obsidian";

vi.mock("obsidian", () => import("./__mocks__/obsidian"));

const manifest = {
  name: "Kioku",
  version: "2.3.0",
  author: "David Sandoval",
  description: "Obsidian plugin bridge for Kioku MCP Server",
};

function isValidBridgeResponse(value: unknown): value is BridgeResponse {
  if (typeof value !== "object" || value === null) return false;
  const response = value as Partial<BridgeResponse>;
  if (typeof response.success !== "boolean") return false;
  if (response.requestId !== undefined && typeof response.requestId !== "string") return false;
  if (response.error !== undefined && typeof response.error !== "string") return false;
  if (response.errorCode !== undefined && typeof response.errorCode !== "string") return false;
  if (response.protocolVersion !== undefined && typeof response.protocolVersion !== "number") {
    return false;
  }
  return true;
}

function makeApp(): App {
  return createMockApp() as unknown as App;
}

describe("Bridge protocol contract", () => {
  describe("runtime message validation", () => {
    const fixtures: BridgeMessage[] = [
      {
        command: "auth",
        payload: {
          minProtocolVersion: PROTOCOL_MIN_VERSION,
          maxProtocolVersion: PROTOCOL_MAX_VERSION,
          clientName: "kioku-mcp-server",
          requestedCapabilities: ["read", "ui-navigation"],
        },
        requestId: "auth-1",
        protocolVersion: PROTOCOL_VERSION,
      },
      {
        command: "open-file",
        payload: { path: "Notes/A.md" },
        requestId: "req-1",
        protocolVersion: PROTOCOL_VERSION,
      },
      {
        command: "get-active-note",
        requestId: "req-2",
        protocolVersion: PROTOCOL_VERSION,
      },
      {
        command: "trigger-command",
        payload: { commandId: "app:toggle-left-sidebar" },
        requestId: "req-3",
        protocolVersion: PROTOCOL_VERSION,
      },
    ];

    it.each(fixtures)("accepts typed message fixture %j", (message) => {
      const result = parseBridgeMessage(message);
      expect(result.ok).toBe(true);
    });

    it("rejects missing request IDs", () => {
      const result = parseBridgeMessage({
        command: "get-active-note",
        protocolVersion: PROTOCOL_VERSION,
      });
      expect(result.ok).toBe(false);
      if (!result.ok) expect(result.error.code).toBe("INVALID_MESSAGE");
    });

    it("rejects unknown commands", () => {
      const result = parseBridgeMessage({
        command: "delete-computer",
        requestId: "req-1",
        protocolVersion: PROTOCOL_VERSION,
      });
      expect(result.ok).toBe(false);
    });

    it("rejects malformed and oversized payload fields", () => {
      expect(
        parseBridgeMessage({
          command: "open-file",
          payload: { path: 123 },
          requestId: "req-1",
          protocolVersion: PROTOCOL_VERSION,
        }).ok
      ).toBe(false);

      expect(
        parseBridgeMessage({
          command: "insert-at-cursor",
          payload: { text: "x".repeat(128 * 1024 + 1) },
          requestId: "req-2",
          protocolVersion: PROTOCOL_VERSION,
        }).ok
      ).toBe(false);
    });

    it("rejects absolute paths, traversal and null bytes", () => {
      expect(isSafeVaultPath("Notes/A.md")).toBe(true);
      expect(isSafeVaultPath("../secret.md")).toBe(false);
      expect(isSafeVaultPath("/etc/passwd")).toBe(false);
      expect(isSafeVaultPath("C:\\Users\\secret.txt")).toBe(false);
      expect(isSafeVaultPath("Notes/A\0.md")).toBe(false);
    });

    it("rejects invalid handshake ranges and capability names", () => {
      expect(
        parseBridgeMessage({
          command: "auth",
          payload: { minProtocolVersion: 4, maxProtocolVersion: 3 },
          requestId: "auth-1",
          protocolVersion: PROTOCOL_VERSION,
        }).ok
      ).toBe(false);

      expect(
        parseBridgeMessage({
          command: "auth",
          payload: {
            minProtocolVersion: 3,
            maxProtocolVersion: 3,
            requestedCapabilities: ["root-access"],
          },
          requestId: "auth-2",
          protocolVersion: PROTOCOL_VERSION,
        }).ok
      ).toBe(false);
    });
  });

  describe("response fixtures", () => {
    const fixtures: BridgeResponse[] = [
      { success: true, data: { ready: true }, requestId: "req-1" },
      {
        success: false,
        errorCode: "INVALID_PAYLOAD",
        error: "[error] [INVALID_PAYLOAD] Invalid payload.",
        requestId: "req-2",
      },
      { success: true, data: null, requestId: "req-3", protocolVersion: PROTOCOL_VERSION },
    ];

    it.each(fixtures)("validates response fixture %j", (response) => {
      expect(isValidBridgeResponse(response)).toBe(true);
    });

    it("rejects a response without success", () => {
      expect(isValidBridgeResponse({ data: {} })).toBe(false);
    });
  });

  describe("handler responses", () => {
    const handlers = createHandlers(makeApp(), DEFAULT_SETTINGS, manifest);
    const handlerNames = Object.keys(handlers);
    const payloads: Record<string, Record<string, unknown> | undefined> = {
      "open-file": { path: "Notes/A.md" },
      "trigger-command": { commandId: "app:toggle-left-sidebar" },
      "insert-at-cursor": { text: "hello" },
      "replace-selection": { text: "hello" },
      "create-note-ui": { path: "Notes/A.md" },
      "scroll-to-block": { blockId: "block-1" },
      "open-in-split": { path: "Notes/A.md" },
      "run-dataview-query": { query: "LIST" },
      "run-templater": { templatePath: "Templates/Test.md" },
      "evaluate-templater-in-file": { notePath: "Notes/A.md" },
      "run-linter": { notePath: "Notes/A.md" },
    };

    it.each(handlerNames)("%s returns a BridgeResponse shape", async (command) => {
      const handler = handlers[command as keyof typeof handlers];
      const result = await handler(payloads[command], "contract-req");
      expect(isValidBridgeResponse(result)).toBe(true);
      expect(result.requestId).toBe("contract-req");
    });
  });

  it("defines one explicit supported protocol version", () => {
    expect(PROTOCOL_MIN_VERSION).toBe(3);
    expect(PROTOCOL_MAX_VERSION).toBe(3);
    expect(PROTOCOL_VERSION).toBe(PROTOCOL_MAX_VERSION);
  });
});
