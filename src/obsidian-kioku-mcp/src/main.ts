import { randomBytes } from "node:crypto";
import { Notice, Plugin, PluginSettingTab, Setting } from "obsidian";
import type { App } from "obsidian";
import { BridgeServer } from "./bridge";
import { createHandlers } from "./handlers";
import { log } from "./logger";
import type { BridgeStatus } from "./status";
import { formatStatusBarText, statusBarCssClass } from "./status";
import type { KiokuSettings } from "./types";
import { DEFAULT_SETTINGS, PROTOCOL_VERSION } from "./types";

export default class KiokuPlugin extends Plugin {
  declare settings: KiokuSettings;
  private bridge: BridgeServer | null = null;
  private bridgeTransition: Promise<void> = Promise.resolve();
  private statusBarItem: HTMLElement | null = null;

  async onload(): Promise<void> {
    await this.loadSettings();
    this.addSettingTab(new KiokuSettingTab(this.app, this));
    this.refreshStatusBarVisibility();

    this.app.workspace.onLayoutReady(() => {
      void this.startBridge();
    });

    this.addCommand({
      id: "kioku-restart-bridge",
      name: "Restart Kioku MCP Bridge",
      callback: () => this.restartBridge(),
    });

    this.addCommand({
      id: "kioku-stop-bridge",
      name: "Stop Kioku MCP Bridge",
      callback: async () => {
        await this.stopBridge();
        new Notice("Kioku MCP Bridge stopped.");
      },
    });

    this.addCommand({
      id: "kioku-start-bridge",
      name: "Start Kioku MCP Bridge",
      callback: async () => {
        const started = await this.startBridge();
        if (started) {
          new Notice("Kioku MCP Bridge started on port " + this.settings.bridgePort);
        }
      },
    });

    this.addCommand({
      id: "kioku-copy-status",
      name: "Copy Kioku bridge status",
      callback: async () => {
        const status: BridgeStatus = {
          running: this.bridge?.isRunning ?? false,
          port: this.settings.bridgePort,
          clients: this.bridge?.clientCount ?? 0,
          protocolVersion: PROTOCOL_VERSION,
          pluginVersion: this.manifest.version,
        };
        await navigator.clipboard.writeText(JSON.stringify(status, null, 2));
        new Notice("Kioku bridge status copied to clipboard.");
      },
    });

    this.addCommand({
      id: "kioku-copy-auth-token",
      name: "Copy Kioku bridge auth token",
      callback: async () => {
        if (!this.settings.authToken) {
          new Notice("Kioku bridge has no auth token configured.");
          return;
        }
        await navigator.clipboard.writeText(this.settings.authToken);
        new Notice("Kioku bridge auth token copied to clipboard.");
      },
    });

    log.info(`Plugin loaded. Bridge configured on port ${this.settings.bridgePort}`);
  }

  onunload(): void {
    this.teardownStatusBar();
    void this.stopBridge();
    log.info("Plugin unloaded.");
  }

  async restartBridge(showNotice = true): Promise<void> {
    await this.enqueueBridgeTransition(async () => {
      await this.stopBridgeInternal();
      this.startBridgeInternal();
    });
    if (showNotice) {
      new Notice("Kioku MCP Bridge restarted on port " + this.settings.bridgePort);
    }
  }

  async startBridge(): Promise<boolean> {
    let started = false;
    await this.enqueueBridgeTransition(async () => {
      if (this.bridge?.isRunning) {
        return;
      }
      await this.stopBridgeInternal();
      started = this.startBridgeInternal();
    });
    return started;
  }

  async stopBridge(): Promise<void> {
    await this.enqueueBridgeTransition(() => this.stopBridgeInternal());
  }

  private enqueueBridgeTransition(operation: () => void | Promise<void>): Promise<void> {
    const next = this.bridgeTransition.then(operation, operation);
    this.bridgeTransition = next.catch((error: unknown) => {
      const message = error instanceof Error ? error.message : "Unknown lifecycle error.";
      log.error(`Bridge lifecycle transition failed: ${message}`);
    });
    return next;
  }

  private startBridgeInternal(): boolean {
    const bridge = new BridgeServer(
      this.settings.bridgePort,
      (message) => {
        if (this.settings.showNotifications) {
          new Notice(`[Kioku] Bridge error: ${message}`);
        }
      },
      (pluginVersion, serverVersion) => {
        if (this.settings.showNotifications) {
          new Notice(
            `[Kioku] Protocol version mismatch. Plugin: v${pluginVersion}, Server: v${serverVersion}. ` +
              "Update the Kioku plugin or server before reconnecting."
          );
        }
      },
      () => this.updateStatusBar(),
      () => this.updateStatusBar(),
      () => this.updateStatusBar(),
      this.settings.authToken,
      this.settings
    );
    bridge.registerHandlers(createHandlers(this.app, this.settings, this.manifest));
    this.bridge = bridge;

    if (!this.settings.authToken) {
      const warning =
        "Kioku bridge is open to other local processes because no auth token is configured.";
      log.warn(warning);
      if (this.settings.showNotifications) {
        new Notice(`[Kioku Security] ${warning}`);
      }
    }

    return bridge.start();
  }

  private async stopBridgeInternal(): Promise<void> {
    const bridge = this.bridge;
    this.bridge = null;
    if (bridge) {
      await bridge.stop();
    }
    this.updateStatusBar();
  }

  refreshStatusBarVisibility(): void {
    if (this.settings.showStatusBar) {
      if (!this.statusBarItem) {
        this.statusBarItem = this.addStatusBarItem();
        this.statusBarItem.addClass("kioku-status");
        this.registerDomEvent(this.statusBarItem, "click", () => {
          void this.restartBridge();
        });
      }
      this.updateStatusBar();
    } else {
      this.teardownStatusBar();
    }
  }

  private teardownStatusBar(): void {
    this.statusBarItem?.remove();
    this.statusBarItem = null;
  }

  private updateStatusBar(): void {
    if (!this.statusBarItem) {
      return;
    }

    const running = this.bridge?.isRunning ?? false;
    const clients = this.bridge?.clientCount ?? 0;

    this.statusBarItem.setText(formatStatusBarText(running, this.settings.bridgePort, clients));
    this.statusBarItem.removeClass("kioku-status-online");
    this.statusBarItem.removeClass("kioku-status-offline");
    this.statusBarItem.addClass(statusBarCssClass(running));
  }

  async loadSettings(): Promise<void> {
    const saved = (await this.loadData()) as Partial<KiokuSettings> | null;
    this.settings = {
      ...DEFAULT_SETTINGS,
      ...(saved ?? {}),
      additionalAllowedCommandIds: saved?.additionalAllowedCommandIds ?? [],
    };
  }

  async saveSettings(): Promise<void> {
    await this.saveData(this.settings);
  }
}

class KiokuSettingTab extends PluginSettingTab {
  plugin: KiokuPlugin;

  constructor(app: App, plugin: KiokuPlugin) {
    super(app, plugin);
    this.plugin = plugin;
  }

  display(): void {
    const { containerEl } = this;
    containerEl.empty();

    containerEl.createEl("h2", { text: "Kioku MCP — Settings" });

    containerEl.createEl("p", {
      text:
        "Kioku connects your Obsidian vault with the Kioku MCP server. The bridge only binds to " +
        "127.0.0.1, but other local processes can still connect unless an auth token is configured.",
      cls: "kioku-description",
    });

    if (!this.plugin.settings.authToken) {
      containerEl.createEl("p", {
        text:
          "Security warning: the bridge currently has no auth token. Generate one and configure " +
          "the same value as KIOKU_BRIDGE_TOKEN in the MCP server.",
        cls: "kioku-security-warning",
      });
    }

    new Setting(containerEl)
      .setName("Bridge port")
      .setDesc(
        "Loopback port used by the bridge. Must match KIOKU_OBSIDIAN_PORT on the server. " +
          "Changing it restarts the bridge."
      )
      .addText((text) =>
        text
          .setPlaceholder("7765")
          .setValue(String(this.plugin.settings.bridgePort))
          .onChange(async (value) => {
            const port = Number.parseInt(value, 10);
            if (port > 0 && port < 65_536 && port !== this.plugin.settings.bridgePort) {
              this.plugin.settings.bridgePort = port;
              await this.plugin.saveSettings();
              await this.plugin.restartBridge(false);
            }
          })
      );

    new Setting(containerEl)
      .setName("Show notifications")
      .setDesc("Show bridge activity and security warnings in Obsidian.")
      .addToggle((toggle) =>
        toggle.setValue(this.plugin.settings.showNotifications).onChange(async (value) => {
          this.plugin.settings.showNotifications = value;
          await this.plugin.saveSettings();
        })
      );

    new Setting(containerEl)
      .setName("Auth token")
      .setDesc(
        "Shared secret required during the capability handshake. Must match KIOKU_BRIDGE_TOKEN. " +
          "Leaving this empty permits any local process to attempt bridge commands."
      )
      .addText((text) => {
        text.inputEl.type = "password";
        text
          .setPlaceholder("(no token — bridge is open locally)")
          .setValue(this.plugin.settings.authToken)
          .onChange(async (value) => {
            this.plugin.settings.authToken = value.trim();
            await this.plugin.saveSettings();
            await this.plugin.restartBridge(false);
          });
      })
      .addButton((button) =>
        button
          .setButtonText("Generate")
          .setTooltip("Generate a random 32-byte token and restart the bridge")
          .onClick(async () => {
            this.plugin.settings.authToken = randomBytes(32).toString("hex");
            await this.plugin.saveSettings();
            await this.plugin.restartBridge(false);
            this.display();
          })
      )
      .addButton((button) =>
        button
          .setButtonText("Copy")
          .setTooltip("Copy the current token")
          .onClick(async () => {
            if (!this.plugin.settings.authToken) {
              new Notice("No Kioku bridge token is configured.");
              return;
            }
            await navigator.clipboard.writeText(this.plugin.settings.authToken);
            new Notice("Kioku bridge auth token copied.");
          })
      );

    containerEl.createEl("h3", { text: "Bridge permissions" });

    this.addRestartingToggle(
      containerEl,
      "Allow editor mutations",
      "Permit dedicated commands that insert text, replace selections, create notes, or reload snippets.",
      this.plugin.settings.allowEditorMutations ?? true,
      (value) => {
        this.plugin.settings.allowEditorMutations = value;
      }
    );

    this.addRestartingToggle(
      containerEl,
      "Allow third-party integrations",
      "Permit Dataview, Templater and per-file Linter commands. Disabled by default.",
      this.plugin.settings.allowThirdPartyIntegrations ?? false,
      (value) => {
        this.plugin.settings.allowThirdPartyIntegrations = value;
      }
    );

    this.addRestartingToggle(
      containerEl,
      "Allow vault-wide operations",
      "Permit operations such as Linter's lint-all-files command. Requires third-party integrations.",
      this.plugin.settings.allowVaultWideOperations ?? false,
      (value) => {
        this.plugin.settings.allowVaultWideOperations = value;
      }
    );

    this.addRestartingToggle(
      containerEl,
      "Allow unsafe custom commands",
      "Permit only the additional command IDs listed below. This does not enable arbitrary command discovery.",
      this.plugin.settings.allowUnsafeCommands ?? false,
      (value) => {
        this.plugin.settings.allowUnsafeCommands = value;
      }
    );

    new Setting(containerEl)
      .setName("Additional allowed command IDs")
      .setDesc(
        "Comma-separated Obsidian command IDs. They are ignored unless unsafe custom commands are enabled."
      )
      .addText((text) =>
        text
          .setPlaceholder("plugin-id:command-id")
          .setValue((this.plugin.settings.additionalAllowedCommandIds ?? []).join(", "))
          .onChange(async (value) => {
            this.plugin.settings.additionalAllowedCommandIds = value
              .split(",")
              .map((entry) => entry.trim())
              .filter(
                (entry, index, entries) => entry.length > 0 && entries.indexOf(entry) === index
              )
              .slice(0, 64);
            await this.plugin.saveSettings();
            await this.plugin.restartBridge(false);
          })
      );

    new Setting(containerEl)
      .setName("Show status bar")
      .setDesc("Show bridge state and connected client count in the status bar.")
      .addToggle((toggle) =>
        toggle.setValue(this.plugin.settings.showStatusBar).onChange(async (value) => {
          this.plugin.settings.showStatusBar = value;
          await this.plugin.saveSettings();
          this.plugin.refreshStatusBarVisibility();
        })
      );
  }

  private addRestartingToggle(
    containerEl: HTMLElement,
    name: string,
    description: string,
    currentValue: boolean,
    update: (value: boolean) => void
  ): void {
    new Setting(containerEl)
      .setName(name)
      .setDesc(description)
      .addToggle((toggle) =>
        toggle.setValue(currentValue).onChange(async (value) => {
          update(value);
          await this.plugin.saveSettings();
          await this.plugin.restartBridge(false);
        })
      );
  }
}
