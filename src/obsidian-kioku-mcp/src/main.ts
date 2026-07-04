import { randomBytes } from "node:crypto";
import { Plugin, PluginSettingTab, Setting, Notice } from "obsidian";
import type { App } from "obsidian";
import { log } from "./logger";
import { BridgeServer } from "./bridge";
import { createHandlers } from "./handlers";
import type { KiokuSettings } from "./types";
import { DEFAULT_SETTINGS, PROTOCOL_VERSION } from "./types";
import type { BridgeStatus } from "./status";
import { formatStatusBarText, statusBarCssClass } from "./status";

export default class KiokuPlugin extends Plugin {
  declare settings: KiokuSettings;
  private bridge: BridgeServer | null = null;
  private statusBarItem: HTMLElement | null = null;

  async onload() {
    await this.loadSettings();
    this.addSettingTab(new KiokuSettingTab(this.app, this));
    this.startBridge();
    this.refreshStatusBarVisibility();

    this.addCommand({
      id: "kioku-restart-bridge",
      name: "Restart Kioku MCP Bridge",
      callback: () => this.restartBridge(),
    });

    this.addCommand({
      id: "kioku-stop-bridge",
      name: "Stop Kioku MCP Bridge",
      callback: () => {
        this.stopBridge();
        new Notice("Kioku MCP Bridge stopped.");
      },
    });

    this.addCommand({
      id: "kioku-start-bridge",
      name: "Start Kioku MCP Bridge",
      callback: () => {
        this.startBridge();
        new Notice("Kioku MCP Bridge started on port " + this.settings.bridgePort);
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

    log.info(`Plugin loaded. Bridge on port ${this.settings.bridgePort}`);
  }

  onunload() {
    this.teardownStatusBar();
    this.stopBridge();
    log.info("Plugin unloaded.");
  }

  private restartBridge() {
    this.stopBridge();
    this.startBridge();
    new Notice("Kioku MCP Bridge restarted on port " + this.settings.bridgePort);
  }

  private startBridge() {
    this.bridge = new BridgeServer(
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
              `Please update the Kioku plugin or server.`
          );
        }
      },
      () => this.updateStatusBar(),
      () => this.updateStatusBar(),
      () => this.updateStatusBar(),
      this.settings.authToken
    );
    this.bridge.registerHandlers(createHandlers(this.app, this.settings, this.manifest));
    this.bridge.start();
  }

  private stopBridge() {
    this.bridge?.stop();
    this.bridge = null;
  }

  refreshStatusBarVisibility() {
    if (this.settings.showStatusBar) {
      if (!this.statusBarItem) {
        this.statusBarItem = this.addStatusBarItem();
        this.statusBarItem.addClass("kioku-status");
        this.registerDomEvent(this.statusBarItem, "click", () => this.restartBridge());
      }
      this.updateStatusBar();
    } else {
      this.teardownStatusBar();
    }
  }

  private teardownStatusBar() {
    this.statusBarItem?.remove();
    this.statusBarItem = null;
  }

  private updateStatusBar() {
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

  async loadSettings() {
    this.settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
  }

  async saveSettings() {
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
        "Kioku connects your Obsidian vault with the Kioku MCP server, " +
        "allowing AI agents (Claude Code, Antigravity CLI) to access your notes.",
      cls: "kioku-description",
    });

    new Setting(containerEl)
      .setName("Bridge Port")
      .setDesc(
        "Port where the plugin listens for Kioku MCP server connections. " +
          "Must match KIOKU_OBSIDIAN_PORT on the server. (Default: 7765)"
      )
      .addText((text) =>
        text
          .setPlaceholder("7765")
          .setValue(String(this.plugin.settings.bridgePort))
          .onChange(async (value) => {
            const port = parseInt(value, 10);
            if (port > 0 && port < 65536) {
              this.plugin.settings.bridgePort = port;
              await this.plugin.saveSettings();
            }
          })
      );

    new Setting(containerEl)
      .setName("Show notifications")
      .setDesc("Shows a notification in Obsidian when the AI agent opens a note.")
      .addToggle((toggle) =>
        toggle.setValue(this.plugin.settings.showNotifications).onChange(async (value) => {
          this.plugin.settings.showNotifications = value;
          await this.plugin.saveSettings();
        })
      );

    new Setting(containerEl)
      .setName("Auth token")
      .setDesc(
        "Optional shared secret required to connect to the bridge. Leave empty to allow " +
          "connections without authentication (default). Must match KIOKU_BRIDGE_TOKEN on the " +
          "server. Restart the bridge after changing this for it to take effect."
      )
      .addText((text) => {
        text.inputEl.type = "password";
        text
          .setPlaceholder("(no token — bridge is open)")
          .setValue(this.plugin.settings.authToken)
          .onChange(async (value) => {
            this.plugin.settings.authToken = value.trim();
            await this.plugin.saveSettings();
          });
      })
      .addButton((button) =>
        button
          .setButtonText("Generate")
          .setTooltip("Generate a random 32-byte token")
          .onClick(async () => {
            this.plugin.settings.authToken = randomBytes(32).toString("hex");
            await this.plugin.saveSettings();
            this.display();
          })
      );

    new Setting(containerEl)
      .setName("Show status bar")
      .setDesc(
        "Shows the Kioku bridge status ([online]/[offline]) in the status bar. " +
          "Click it to restart the bridge."
      )
      .addToggle((toggle) =>
        toggle.setValue(this.plugin.settings.showStatusBar).onChange(async (value) => {
          this.plugin.settings.showStatusBar = value;
          await this.plugin.saveSettings();
          this.plugin.refreshStatusBarVisibility();
        })
      );
  }
}
