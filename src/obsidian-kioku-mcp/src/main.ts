import { Plugin, PluginSettingTab, Setting, Notice } from "obsidian";
import type { App } from "obsidian";
import { log } from "./logger";
import { BridgeServer } from "./bridge";
import { createHandlers } from "./handlers";
import type { KiokuSettings } from "./types";
import { DEFAULT_SETTINGS } from "./types";

export default class KiokuPlugin extends Plugin {
  declare settings: KiokuSettings;
  private bridge: BridgeServer | null = null;

  async onload() {
    await this.loadSettings();
    this.addSettingTab(new KiokuSettingTab(this.app, this));
    this.startBridge();

    this.addCommand({
      id: "kioku-restart-bridge",
      name: "Restart Kioku MCP Bridge",
      callback: () => {
        this.stopBridge();
        this.startBridge();
        new Notice("Kioku MCP Bridge restarted on port " + this.settings.bridgePort);
      },
    });

    log.info(`Plugin loaded. Bridge on port ${this.settings.bridgePort}`);
  }

  onunload() {
    this.stopBridge();
    log.info("Plugin unloaded.");
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
      }
    );
    this.bridge.registerHandlers(createHandlers(this.app, this.settings, this.manifest));
    this.bridge.start();
  }

  private stopBridge() {
    this.bridge?.stop();
    this.bridge = null;
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
  }
}
