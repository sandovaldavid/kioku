import { describe, expect, it, vi } from "vitest";
import type { App } from "obsidian";
import {
  executeObsidianCommand,
  getObsidianVersion,
  getThirdPartyPluginApi,
  getVaultBasePath,
  listInstalledPlugins,
} from "./obsidian-compat";
import { createMockApp } from "./test-utils";

vi.mock("obsidian", () => import("./__mocks__/obsidian"));

function app(options: Parameters<typeof createMockApp>[0] = {}): App {
  return createMockApp(options) as unknown as App;
}

describe("Obsidian compatibility adapter", () => {
  it("uses FileSystemAdapter only when the public desktop adapter is available", () => {
    const desktop = app({ vaultPath: "/vault" });
    expect(getVaultBasePath(desktop)).toBe("/vault");

    const unsupported = app();
    (unsupported.vault as unknown as { adapter: unknown }).adapter = {};
    expect(getVaultBasePath(unsupported)).toBeNull();
  });

  it("degrades gracefully when command internals are unavailable", () => {
    const current = app({ commands: new Map([["app:test", true]]) });
    expect(executeObsidianCommand(current, "app:test")).toBe(true);

    delete (current as unknown as { commands?: unknown }).commands;
    expect(executeObsidianCommand(current, "app:test")).toBe(false);
  });

  it("reads version and third-party APIs through guarded capability checks", () => {
    const api = { query: () => undefined };
    const current = app({ plugins: { plugins: { dataview: api } } });
    expect(getObsidianVersion(current)).toBe("1.13.1");
    expect(getThirdPartyPluginApi(current, "dataview")).toBe(api);
    expect(getThirdPartyPluginApi(current, "missing")).toBeNull();
  });

  it("lists manifests deterministically and preserves enabled state", () => {
    const current = app({
      plugins: {
        manifests: {
          zeta: { name: "Zeta", version: "1.0.0" },
          alpha: { name: "Alpha", version: "2.0.0" },
        },
        enabledPlugins: new Set(["zeta"]),
      },
    });

    expect(listInstalledPlugins(current)).toEqual([
      {
        id: "alpha",
        name: "Alpha",
        version: "2.0.0",
        author: undefined,
        description: undefined,
        enabled: false,
      },
      {
        id: "zeta",
        name: "Zeta",
        version: "1.0.0",
        author: undefined,
        description: undefined,
        enabled: true,
      },
    ]);
  });
});
