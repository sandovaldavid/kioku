import { App, Plugin, PluginSettingTab, Setting, Notice } from "obsidian";
import WebSocket, { WebSocketServer } from "ws";

// ── Types ────────────────────────────────────────────────────────────────────

interface KiokuSettings {
  /** Port where the plugin's WebSocket server listens. */
  bridgePort: number;
  /** If true, shows notifications in Obsidian when the agent performs actions. */
  showNotifications: boolean;
}

const DEFAULT_SETTINGS: KiokuSettings = {
  bridgePort: 7765,
  showNotifications: true,
};

interface BridgeMessage {
  command: string;
  payload?: Record<string, unknown>;
  requestId?: string;
}

interface BridgeResponse {
  requestId?: string;
  success: boolean;
  data?: unknown;
  error?: string;
}

// ── Main Plugin ──────────────────────────────────────────────────────────

export default class KiokuPlugin extends Plugin {
  declare settings: KiokuSettings;
  private wss: WebSocketServer | null = null;
  private clients = new Set<WebSocket>();

  async onload() {
    await this.loadSettings();

    // Add settings tab
    this.addSettingTab(new KiokuSettingTab(this.app, this));

    // Start WebSocket server
    this.startBridgeServer();

    // Palette command: restart bridge
    this.addCommand({
      id: "kioku-restart-bridge",
      name: "Restart Kioku MCP Bridge",
      callback: () => {
        this.stopBridgeServer();
        this.startBridgeServer();
        new Notice("Kioku MCP Bridge restarted on port " + this.settings.bridgePort);
      },
    });

    console.log(`[Kioku] Plugin loaded. Bridge on port ${this.settings.bridgePort}`);
  }

  onunload() {
    this.stopBridgeServer();
    console.log("[Kioku] Plugin unloaded.");
  }

  // ── WebSocket Bridge ────────────────────────────────────────────────────────

  private startBridgeServer() {
    try {
      this.wss = new WebSocketServer({
        host: "127.0.0.1", // Localhost only — never expose externally
        port: this.settings.bridgePort,
      });

      this.wss.on("connection", (ws) => {
        this.clients.add(ws);
        console.log(`[Kioku] Kioku MCP Server connected. Clients: ${this.clients.size}`);

        ws.on("message", async (data) => {
          try {
            const raw = Buffer.isBuffer(data)
              ? data.toString("utf8")
              : Array.isArray(data)
                ? Buffer.concat(data).toString("utf8")
                : Buffer.from(data).toString("utf8");
            const msg = JSON.parse(raw) as BridgeMessage;
            const response = await this.handleCommand(msg);
            ws.send(JSON.stringify(response));
          } catch (err) {
            ws.send(JSON.stringify({ success: false, error: String(err) }));
          }
        });

        ws.on("close", () => {
          this.clients.delete(ws);
          console.log(`[Kioku] Client disconnected. Clients: ${this.clients.size}`);
        });

        ws.on("error", (err) => {
          console.error("[Kioku] WebSocket error:", err.message);
        });
      });

      this.wss.on("error", (err) => {
        console.error(`[Kioku] Could not start the bridge: ${err.message}`);
        if (this.settings.showNotifications) {
          new Notice(`❌ Kioku MCP Bridge error: ${err.message}`);
        }
      });

      console.log(`[Kioku] Bridge listening on 127.0.0.1:${this.settings.bridgePort}`);
    } catch (err) {
      console.error("[Kioku] Error starting bridge:", err);
    }
  }

  private stopBridgeServer() {
    for (const client of this.clients) {
      client.close();
    }
    this.clients.clear();

    if (this.wss) {
      this.wss.close();
      this.wss = null;
    }
  }

  // ── Command Handler ─────────────────────────────────────────────────────────

  private async handleCommand(msg: BridgeMessage): Promise<BridgeResponse> {
    const { command, payload, requestId } = msg;

    try {
      switch (command) {
        case "open-file":
          return await this.cmdOpenFile(payload as { path: string }, requestId);

        case "get-active-note":
          return this.cmdGetActiveNote(requestId);

        case "get-vault-path":
          return this.cmdGetVaultPath(requestId);

        case "is-obsidian-ready":
          return { requestId, success: true, data: { ready: true } };

        case "get-app-version":
          return {
            requestId,
            success: true,
            data: {
              obsidianVersion: (this.app as any).version ?? "unknown",
              kiokuVersion: this.manifest.version,
            },
          };

        case "get-open-notes":
          return this.cmdGetOpenNotes(requestId);

        case "trigger-command":
          return this.cmdTriggerCommand(payload as { commandId: string }, requestId);

        default:
          return { requestId, success: false, error: `Unknown command: ${command}` };
      }
    } catch (err) {
      return { requestId, success: false, error: String(err) };
    }
  }

  // ── Command Implementation ─────────────────────────────────────────────

  private async cmdOpenFile(
    payload: { path: string },
    requestId?: string
  ): Promise<BridgeResponse> {
    const { path } = payload;
    const file = this.app.vault.getFileByPath(path);

    if (!file) {
      return { requestId, success: false, error: `File not found: ${path}` };
    }

    await this.app.workspace.openLinkText(path, "", false);

    if (this.settings.showNotifications) {
      new Notice(`📝 Kioku opened: ${file.basename}`);
    }

    return { requestId, success: true, data: { path, name: file.basename } };
  }

  private cmdGetActiveNote(requestId?: string): BridgeResponse {
    const activeFile = this.app.workspace.getActiveFile();

    if (!activeFile) {
      return { requestId, success: true, data: null };
    }

    const cache = this.app.metadataCache.getFileCache(activeFile);
    return {
      requestId,
      success: true,
      data: {
        path: activeFile.path,
        name: activeFile.basename,
        tags: cache?.frontmatter?.tags ?? [],
        status: cache?.frontmatter?.status ?? null,
      },
    };
  }

  private cmdGetVaultPath(requestId?: string): BridgeResponse {
    const adapter = this.app.vault.adapter;
    const vaultPath = (adapter as any).basePath ?? this.app.vault.getName();

    return {
      requestId,
      success: true,
      data: { vaultPath, vaultName: this.app.vault.getName() },
    };
  }

  private cmdGetOpenNotes(requestId?: string): BridgeResponse {
    const openFiles: Array<{ path: string; name: string }> = [];

    this.app.workspace.iterateAllLeaves((leaf) => {
      if (leaf.view.getViewType() === "markdown") {
        const file = (leaf.view as any).file;
        if (file) {
          openFiles.push({ path: file.path, name: file.basename });
        }
      }
    });

    return { requestId, success: true, data: openFiles };
  }

  private cmdTriggerCommand(payload: { commandId: string }, requestId?: string): BridgeResponse {
    const { commandId } = payload;
    const commands = (this.app as any).commands;

    if (!commands || !commands.executeCommandById) {
      return { requestId, success: false, error: "The Obsidian command API is not available." };
    }

    const executed = commands.executeCommandById(commandId);
    if (!executed) {
      return {
        requestId,
        success: false,
        error: `Command not found or not executable: '${commandId}'`,
      };
    }

    return { requestId, success: true, data: { commandId } };
  }

  // ── Configuration ───────────────────────────────────────────────────────────

  async loadSettings() {
    this.settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
  }

  async saveSettings() {
    await this.saveData(this.settings);
  }
}

// ── Configuration Settings Tab ────────────────────────────────────────────────

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
