import { FileSystemAdapter } from "obsidian";
import type { App } from "obsidian";
import type { PluginManifest } from "./types";

interface InternalCommandRegistry {
  executeCommandById?: (commandId: string) => boolean;
}

interface InternalPluginRegistry {
  manifests?: Record<string, PluginManifest>;
  enabledPlugins?: Set<string>;
  plugins?: Record<string, unknown>;
}

interface InternalAppSurface {
  version?: unknown;
  commands?: InternalCommandRegistry;
  plugins?: InternalPluginRegistry;
}

export interface InstalledPluginInfo extends PluginManifest {
  id: string;
  enabled: boolean;
}

function internalSurface(app: App): InternalAppSurface {
  return app as App & InternalAppSurface;
}

export function getVaultBasePath(app: App): string | null {
  const adapter = app.vault.adapter;
  return adapter instanceof FileSystemAdapter ? adapter.getBasePath() : null;
}

export function getObsidianVersion(app: App): string | null {
  const version = internalSurface(app).version;
  return typeof version === "string" && version.length > 0 ? version : null;
}

export function executeObsidianCommand(app: App, commandId: string): boolean {
  const execute = internalSurface(app).commands?.executeCommandById;
  if (typeof execute !== "function") {
    return false;
  }

  try {
    return execute.call(internalSurface(app).commands, commandId);
  } catch {
    return false;
  }
}

export function getThirdPartyPluginApi<T>(app: App, pluginId: string): T | null {
  const plugin = internalSurface(app).plugins?.plugins?.[pluginId];
  return plugin && typeof plugin === "object" ? (plugin as T) : null;
}

export function listInstalledPlugins(app: App): InstalledPluginInfo[] {
  const registry = internalSurface(app).plugins;
  const manifests = registry?.manifests;
  if (!manifests || typeof manifests !== "object") {
    return [];
  }

  const enabled = registry.enabledPlugins ?? new Set<string>();
  return Object.entries(manifests)
    .map(([id, manifest]) => ({
      id,
      name: manifest.name,
      version: manifest.version,
      author: manifest.author,
      description: manifest.description,
      enabled: enabled.has(id),
    }))
    .sort((left, right) => left.name.localeCompare(right.name));
}
