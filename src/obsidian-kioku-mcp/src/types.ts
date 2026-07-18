import type { App } from "obsidian";

export interface PluginManifest {
  name: string;
  version: string;
  author?: string;
  description?: string;
}

/**
 * Internal Obsidian App interface extension.
 *
 * These APIs are undocumented and may change between Obsidian versions. Every caller must
 * handle unavailable commands/plugins without exposing implementation details to bridge clients.
 */
export interface KiokuApp extends App {
  version: string;
  commands: {
    executeCommandById(commandId: string): boolean;
  };
  plugins: {
    manifests: Record<string, PluginManifest>;
    enabledPlugins: Set<string>;
    plugins: Record<string, unknown>;
  };
}

export function asKiokuApp(app: App): KiokuApp {
  return app as unknown as KiokuApp;
}

export interface KiokuDataAdapter {
  basePath: string;
}

export interface KiokuSettings {
  bridgePort: number;
  showNotifications: boolean;
  showStatusBar: boolean;
  authToken: string;
  /** Allow the dedicated cursor/selection/note mutation commands. */
  allowEditorMutations?: boolean;
  /** Allow Dataview, Templater and per-file Linter integrations. */
  allowThirdPartyIntegrations?: boolean;
  /** Allow operations that may mutate the entire vault, such as lint-all-files. */
  allowVaultWideOperations?: boolean;
  /** Allow explicitly listed command IDs outside Kioku's built-in safe command set. */
  allowUnsafeCommands?: boolean;
  /** Additional command IDs allowed only while unsafe command mode is enabled. */
  additionalAllowedCommandIds?: string[];
}

export const DEFAULT_SETTINGS: KiokuSettings = {
  bridgePort: 7765,
  showNotifications: true,
  showStatusBar: true,
  authToken: "",
  allowEditorMutations: true,
  allowThirdPartyIntegrations: false,
  allowVaultWideOperations: false,
  allowUnsafeCommands: false,
  additionalAllowedCommandIds: [],
};

export const PROTOCOL_MIN_VERSION = 3;
export const PROTOCOL_MAX_VERSION = 3;
export const PROTOCOL_VERSION = PROTOCOL_MAX_VERSION;

export const BRIDGE_CAPABILITIES = [
  "read",
  "ui-navigation",
  "editor-mutation",
  "third-party-dataview",
  "third-party-templater",
  "third-party-linter",
  "vault-wide",
  "unsafe-command",
] as const;

export type BridgeCapability = (typeof BRIDGE_CAPABILITIES)[number];

export type BridgeErrorCode =
  | "INVALID_MESSAGE"
  | "INVALID_PAYLOAD"
  | "UNAUTHORIZED"
  | "HANDSHAKE_REQUIRED"
  | "HANDSHAKE_ALREADY_COMPLETED"
  | "UNSUPPORTED_PROTOCOL"
  | "CAPABILITY_DENIED"
  | "COMMAND_DENIED"
  | "UNKNOWN_COMMAND"
  | "RATE_LIMITED"
  | "CLIENT_LIMIT"
  | "REQUEST_TIMEOUT"
  | "REPLAY_DETECTED"
  | "BACKPRESSURE"
  | "COMMAND_FAILED"
  | "INTERNAL_ERROR";

export interface AuthPayload {
  token?: string;
  minProtocolVersion: number;
  maxProtocolVersion: number;
  clientName?: string;
  clientVersion?: string;
  requestedCapabilities?: BridgeCapability[];
}

export interface CommandPayloadMap {
  auth: AuthPayload;
  "open-file": { path: string };
  "get-active-note": undefined;
  "get-vault-path": undefined;
  "is-obsidian-ready": undefined;
  "get-app-version": undefined;
  "get-open-notes": undefined;
  "trigger-command": { commandId: string };
  "toggle-reading-mode": undefined;
  "get-selection": undefined;
  "fold-all-headings": undefined;
  "unfold-all-headings": undefined;
  "reload-snippets": undefined;
  "insert-at-cursor": { text: string };
  "replace-selection": { text: string };
  "create-note-ui": { path: string };
  "scroll-to-block": { blockId: string };
  "open-in-split": { path: string };
  "run-dataview-query": { query: string };
  "run-templater": { templatePath: string; targetNote?: string };
  "evaluate-templater-in-file": { notePath: string };
  "run-linter": { notePath?: string };
  "run-linter-vault": undefined;
  "get-installed-plugins": undefined;
}

export type BridgeCommand = keyof CommandPayloadMap;
export type RuntimeCommand = Exclude<BridgeCommand, "auth">;

export type BridgeMessage = {
  [Command in BridgeCommand]: {
    command: Command;
    payload?: CommandPayloadMap[Command];
    requestId: string;
    protocolVersion: number;
  };
}[BridgeCommand];

export interface HandshakeData {
  negotiatedProtocolVersion: number;
  minProtocolVersion: number;
  maxProtocolVersion: number;
  capabilities: BridgeCapability[];
  authenticationRequired: boolean;
}

export interface BridgeResponse {
  requestId?: string;
  success: boolean;
  data?: unknown;
  error?: string;
  errorCode?: BridgeErrorCode;
  protocolVersion?: number;
}

export type CommandHandler = (
  payload: Record<string, unknown> | undefined,
  requestId?: string
) => BridgeResponse | Promise<BridgeResponse>;
