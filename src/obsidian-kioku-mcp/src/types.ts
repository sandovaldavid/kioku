import type { App } from "obsidian";

export interface PluginManifest {
  name: string;
  version: string;
  author?: string;
  description?: string;
}

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

export interface KiokuDataAdapter {
  basePath: string;
}

export function asKiokuApp(app: App): KiokuApp {
  return app as unknown as KiokuApp;
}

export interface KiokuSettings {
  bridgePort: number;
  showNotifications: boolean;
}

export const DEFAULT_SETTINGS: KiokuSettings = {
  bridgePort: 7765,
  showNotifications: true,
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
