import type { BridgeStatus } from "./status";
import type { KiokuSettings } from "./types";
import { DEFAULT_SETTINGS } from "./types";

function validPort(value: unknown): value is number {
  return typeof value === "number" && Number.isInteger(value) && value > 0 && value < 65_536;
}

function booleanOrDefault(value: unknown, fallback: boolean): boolean {
  return typeof value === "boolean" ? value : fallback;
}

export function normalizeSettings(saved: Partial<KiokuSettings> | null): KiokuSettings {
  const additionalAllowedCommandIds = Array.isArray(saved?.additionalAllowedCommandIds)
    ? saved.additionalAllowedCommandIds
        .filter((entry): entry is string => typeof entry === "string")
        .map((entry) => entry.trim())
        .filter((entry, index, entries) => entry.length > 0 && entries.indexOf(entry) === index)
        .slice(0, 64)
    : [];

  return {
    bridgePort: validPort(saved?.bridgePort) ? saved.bridgePort : DEFAULT_SETTINGS.bridgePort,
    showNotifications: booleanOrDefault(
      saved?.showNotifications,
      DEFAULT_SETTINGS.showNotifications
    ),
    showStatusBar: booleanOrDefault(saved?.showStatusBar, DEFAULT_SETTINGS.showStatusBar),
    authToken: typeof saved?.authToken === "string" ? saved.authToken.trim() : "",
    allowEditorMutations: booleanOrDefault(
      saved?.allowEditorMutations,
      DEFAULT_SETTINGS.allowEditorMutations ?? true
    ),
    allowThirdPartyIntegrations: booleanOrDefault(
      saved?.allowThirdPartyIntegrations,
      DEFAULT_SETTINGS.allowThirdPartyIntegrations ?? false
    ),
    allowVaultWideOperations: booleanOrDefault(
      saved?.allowVaultWideOperations,
      DEFAULT_SETTINGS.allowVaultWideOperations ?? false
    ),
    allowUnsafeCommands: booleanOrDefault(
      saved?.allowUnsafeCommands,
      DEFAULT_SETTINGS.allowUnsafeCommands ?? false
    ),
    additionalAllowedCommandIds,
  };
}

export function formatBridgeStatusDescription(status: BridgeStatus): string {
  const state = status.running ? "Running" : "Stopped";
  const clients =
    status.clients === 1 ? "1 connected client" : `${status.clients} connected clients`;
  const authentication = status.authenticationEnabled
    ? "authentication enabled"
    : "authentication disabled";
  const protocol =
    status.minProtocolVersion === status.maxProtocolVersion
      ? `protocol v${status.protocolVersion}`
      : `protocol v${status.minProtocolVersion}–${status.maxProtocolVersion}`;

  return `${state} on 127.0.0.1:${status.port}; ${authentication}; ${clients}; ${protocol}; plugin v${status.pluginVersion}.`;
}
