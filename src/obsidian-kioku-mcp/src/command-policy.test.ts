import { describe, expect, it } from "vitest";
import { authorizeCommand, getBridgeCapabilities } from "./command-policy";
import { DEFAULT_SETTINGS, PROTOCOL_VERSION, type BridgeMessage } from "./types";

function message(
  command: Exclude<BridgeMessage["command"], "auth">,
  payload?: Record<string, unknown>
): Extract<BridgeMessage, { command: Exclude<BridgeMessage["command"], "auth"> }> {
  return {
    command,
    payload,
    requestId: "req-1",
    protocolVersion: PROTOCOL_VERSION,
  } as Extract<BridgeMessage, { command: Exclude<BridgeMessage["command"], "auth"> }>;
}

describe("command authorization", () => {
  it("advertises only secure-default capabilities", () => {
    expect(getBridgeCapabilities(DEFAULT_SETTINGS)).toEqual([
      "read",
      "ui-navigation",
      "editor-mutation",
    ]);
  });

  it("allows built-in UI command IDs", () => {
    const result = authorizeCommand(
      message("trigger-command", { commandId: "app:toggle-left-sidebar" }),
      DEFAULT_SETTINGS
    );
    expect(result.allowed).toBe(true);
    expect(result.risk).toBe("ui-navigation");
  });

  it("denies arbitrary and third-party command IDs by default", () => {
    const result = authorizeCommand(
      message("trigger-command", { commandId: "third-party:delete-everything" }),
      DEFAULT_SETTINGS
    );
    expect(result.allowed).toBe(false);
    expect(result.code).toBe("COMMAND_DENIED");
  });

  it("requires both unsafe mode and an explicit command ID", () => {
    const commandId = "third-party:custom-command";
    expect(
      authorizeCommand(
        message("trigger-command", { commandId }),
        { ...DEFAULT_SETTINGS, allowUnsafeCommands: true }
      ).allowed
    ).toBe(false);

    expect(
      authorizeCommand(
        message("trigger-command", { commandId }),
        {
          ...DEFAULT_SETTINGS,
          allowUnsafeCommands: true,
          additionalAllowedCommandIds: [commandId],
        }
      ).allowed
    ).toBe(true);
  });

  it("gates editor mutations independently", () => {
    const result = authorizeCommand(
      message("insert-at-cursor", { text: "hello" }),
      { ...DEFAULT_SETTINGS, allowEditorMutations: false }
    );
    expect(result.allowed).toBe(false);
    expect(result.code).toBe("CAPABILITY_DENIED");
  });

  it("gates third-party integrations independently", () => {
    const denied = authorizeCommand(
      message("run-dataview-query", { query: "LIST" }),
      DEFAULT_SETTINGS
    );
    expect(denied.allowed).toBe(false);

    const allowed = authorizeCommand(
      message("run-dataview-query", { query: "LIST" }),
      { ...DEFAULT_SETTINGS, allowThirdPartyIntegrations: true }
    );
    expect(allowed.allowed).toBe(true);
  });

  it("requires third-party and vault-wide permissions for lint-all", () => {
    expect(
      authorizeCommand(
        message("run-linter-vault"),
        { ...DEFAULT_SETTINGS, allowVaultWideOperations: true }
      ).allowed
    ).toBe(false);

    expect(
      authorizeCommand(
        message("run-linter-vault"),
        {
          ...DEFAULT_SETTINGS,
          allowThirdPartyIntegrations: true,
          allowVaultWideOperations: true,
        }
      ).allowed
    ).toBe(true);
  });
});
