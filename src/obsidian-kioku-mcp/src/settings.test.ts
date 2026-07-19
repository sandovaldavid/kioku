import { describe, expect, it } from "vitest";
import { formatBridgeStatusDescription, normalizeSettings } from "./settings";

describe("normalizeSettings", () => {
  it("migrates incomplete legacy data to secure current defaults", () => {
    expect(normalizeSettings({ bridgePort: 9000, showNotifications: false })).toEqual({
      bridgePort: 9000,
      showNotifications: false,
      showStatusBar: true,
      authToken: "",
      allowEditorMutations: true,
      allowThirdPartyIntegrations: false,
      allowVaultWideOperations: false,
      allowUnsafeCommands: false,
      additionalAllowedCommandIds: [],
    });
  });

  it("rejects invalid ports and normalizes custom command IDs", () => {
    expect(
      normalizeSettings({
        bridgePort: 70_000,
        authToken: " token ",
        additionalAllowedCommandIds: [" test:one ", "test:one", "", "test:two"],
      })
    ).toMatchObject({
      bridgePort: 7765,
      authToken: "token",
      additionalAllowedCommandIds: ["test:one", "test:two"],
    });
  });
});

describe("formatBridgeStatusDescription", () => {
  it("reports lifecycle, authentication, clients, protocol and plugin compatibility", () => {
    expect(
      formatBridgeStatusDescription({
        running: true,
        port: 7765,
        clients: 2,
        protocolVersion: 3,
        minProtocolVersion: 3,
        maxProtocolVersion: 3,
        pluginVersion: "2.3.0",
        authenticationEnabled: true,
      })
    ).toBe(
      "Running on 127.0.0.1:7765; authentication enabled; 2 connected clients; protocol v3; plugin v2.3.0."
    );
  });
});
