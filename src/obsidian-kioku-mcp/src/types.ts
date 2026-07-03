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
 * Note: These are undocumented internal APIs that may change between Obsidian versions.
 * We use type assertions to access them, but provide fallbacks where possible.
 *
 * - `version`: Obsidian app version (used for diagnostics)
 * - `commands`: Command registry for executing Obsidian commands
 * - `plugins`: Plugin manager for accessing other plugins' APIs
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

/**
 * Type assertion helper for accessing internal Obsidian APIs.
 *
 * WARNING: This accesses undocumented internal APIs. If Obsidian changes
 * these interfaces, the plugin may fail gracefully. We check for null/undefined
 * where possible and provide fallback behavior.
 */
export function asKiokuApp(app: App): KiokuApp {
  return app as unknown as KiokuApp;
}

/**
 * Internal Obsidian DataAdapter interface extension.
 *
 * Note: This is an undocumented internal API that provides access to the vault's
 * base path. It may change between Obsidian versions. We use it to determine
 * the absolute path to the vault on disk.
 *
 * Fallback: If this API is unavailable, we return "unknown" for the vault path.
 */
export interface KiokuDataAdapter {
  basePath: string;
}

export interface KiokuSettings {
  bridgePort: number;
  showNotifications: boolean;
  showStatusBar: boolean;
}

export const DEFAULT_SETTINGS: KiokuSettings = {
  bridgePort: 7765,
  showNotifications: true,
  showStatusBar: true,
};

export const PROTOCOL_VERSION = 1;

export interface BridgeMessage {
  command: string;
  payload?: Record<string, unknown>;
  requestId?: string;
  protocolVersion?: number;
}

export interface BridgeResponse {
  requestId?: string;
  success: boolean;
  data?: unknown;
  error?: string;
  protocolVersion?: number;
}

export type CommandHandler = (
  payload: Record<string, unknown> | undefined,
  requestId?: string
) => BridgeResponse | Promise<BridgeResponse>;
