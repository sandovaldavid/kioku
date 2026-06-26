import { App, Plugin, PluginSettingTab, Setting, Notice, MarkdownView } from "obsidian";
import WebSocket, { WebSocketServer } from "ws";
import { log } from "./logger";

// Types

interface PluginManifest {
  name: string;
  version: string;
  author?: string;
  description?: string;
}

interface KiokuApp extends App {
  version: string;
  commands: {
    executeCommandById(commandId: string): boolean;
  };
  plugins: {
    manifests: Record<string, PluginManifest>;
    enabledPlugins: Set<string>;
    plugins: Record<string, any>;
  };
}

interface KiokuDataAdapter {
  basePath: string;
}

function asKiokuApp(app: App): KiokuApp {
  return app as unknown as KiokuApp;
}

interface KiokuSettings {
  bridgePort: number;
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

type CommandHandler = (
  payload: Record<string, unknown> | undefined,
  requestId?: string
) => BridgeResponse | Promise<BridgeResponse>;

// Main Plugin

export default class KiokuPlugin extends Plugin {
  declare settings: KiokuSettings;
  private wss: WebSocketServer | null = null;
  private clients = new Set<WebSocket>();
  private handlers: Record<string, CommandHandler> = {};

  async onload() {
    await this.loadSettings();
    this.registerHandlers();
    this.addSettingTab(new KiokuSettingTab(this.app, this));
    this.startBridgeServer();

    this.addCommand({
      id: "kioku-restart-bridge",
      name: "Restart Kioku MCP Bridge",
      callback: () => {
        this.stopBridgeServer();
        this.startBridgeServer();
        new Notice("Kioku MCP Bridge restarted on port " + this.settings.bridgePort);
      },
    });

    log.info(`Plugin loaded. Bridge on port ${this.settings.bridgePort}`);
  }

  onunload() {
    this.stopBridgeServer();
    log.info("Plugin unloaded.");
  }

  // Handler Registry

  private registerHandlers() {
    this.handlers = {
      // Obsidian UI Bridge
      "open-file": (p) => this.cmdOpenFile(p as { path: string }),
      "get-active-note": () => this.cmdGetActiveNote(),
      "get-vault-path": () => this.cmdGetVaultPath(),
      "is-obsidian-ready": () => ({ success: true, data: { ready: true } }),
      "get-app-version": () => ({
        success: true,
        data: {
          obsidianVersion: asKiokuApp(this.app).version,
          kiokuVersion: this.manifest.version,
        },
      }),
      "get-open-notes": () => this.cmdGetOpenNotes(),

      // Command execution
      "trigger-command": (p) => this.cmdTriggerCommand(p as { commandId: string }),
      "toggle-reading-mode": () => this.cmdToggleReadingMode(),
      "get-selection": () => this.cmdGetSelection(),
      "fold-all-headings": () => this.cmdFoldAll(),
      "unfold-all-headings": () => this.cmdUnfoldAll(),
      "reload-snippets": () => this.cmdReloadSnippets(),

      // Editor
      "insert-at-cursor": (p) => this.cmdInsertAtCursor(p as { text: string }),
      "replace-selection": (p) => this.cmdReplaceSelection(p as { text: string }),
      "create-note-ui": (p) => this.cmdCreateNoteUi(p as { path: string }),
      "scroll-to-block": (p) => this.cmdScrollToBlock(p as { blockId: string }),
      "open-in-split": (p) => this.cmdOpenInSplit(p as { path: string }),

      // Plugin Integrations
      "run-dataview-query": (p) => this.cmdRunDataviewQuery(p as { query: string }),
      "run-templater": (p) =>
        this.cmdRunTemplater(p as { templatePath: string; targetNote?: string }),
      "run-linter": (p) => this.cmdRunLinter(p as { notePath?: string }),
      "run-linter-vault": () => this.cmdRunLinterVault(),
      "get-installed-plugins": () => this.cmdGetInstalledPlugins(),
    };
  }

  // WebSocket Bridge

  private startBridgeServer() {
    try {
      this.wss = new WebSocketServer({
        host: "127.0.0.1",
        port: this.settings.bridgePort,
      });

      this.wss.on("connection", (ws) => {
        this.clients.add(ws);
        log.info(`Kioku MCP Server connected. Clients: ${this.clients.size}`);

        ws.on("message", async (data) => {
          try {
            const raw = Buffer.isBuffer(data)
              ? data.toString("utf8")
              : Array.isArray(data)
                ? Buffer.concat(data).toString("utf8")
                : Buffer.from(data).toString("utf8");
            const msg = JSON.parse(raw) as BridgeMessage;
            const response = await this.dispatch(msg);
            ws.send(JSON.stringify(response));
          } catch (err) {
            ws.send(JSON.stringify({ success: false, error: String(err) }));
          }
        });

        ws.on("close", () => {
          this.clients.delete(ws);
          log.info(`Client disconnected. Clients: ${this.clients.size}`);
        });

        ws.on("error", (err) => {
          log.error(`WebSocket error: ${err.message}`);
        });
      });

      this.wss.on("error", (err) => {
        log.error(`Could not start the bridge: ${err.message}`);
        if (this.settings.showNotifications) {
          new Notice(`[error] Kioku MCP Bridge: ${err.message}`);
        }
      });

      log.info(`Bridge listening on 127.0.0.1:${this.settings.bridgePort}`);
    } catch (err) {
      log.error("Error starting bridge:", err);
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

  private async dispatch(msg: BridgeMessage): Promise<BridgeResponse> {
    const { command, payload, requestId } = msg;
    const handler = this.handlers[command];

    if (!handler) {
      return { requestId, success: false, error: `Unknown command: ${command}` };
    }

    try {
      const result = await handler(payload, requestId);
      return { ...result, requestId };
    } catch (err) {
      return { requestId, success: false, error: String(err) };
    }
  }

  // Command implementations — Obsidian UI Bridge

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
      new Notice(`Kioku opened: ${file.basename}`);
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
    const vaultPath = (this.app.vault.adapter as unknown as KiokuDataAdapter).basePath;

    return {
      requestId,
      success: true,
      data: { vaultPath, vaultName: this.app.vault.getName() },
    };
  }

  private cmdGetOpenNotes(requestId?: string): BridgeResponse {
    const openFiles: Array<{ path: string; name: string }> = [];

    this.app.workspace.iterateAllLeaves((leaf) => {
      if (leaf.view instanceof MarkdownView) {
        const file = leaf.view.file;
        if (file) {
          openFiles.push({ path: file.path, name: file.basename });
        }
      }
    });

    return { requestId, success: true, data: openFiles };
  }

  private cmdTriggerCommand(payload: { commandId: string }, requestId?: string): BridgeResponse {
    const { commandId } = payload;
    const executed = asKiokuApp(this.app).commands.executeCommandById(commandId);
    if (!executed) {
      return {
        requestId,
        success: false,
        error: `Command not found or not executable: '${commandId}'`,
      };
    }
    return { requestId, success: true, data: { commandId } };
  }

  private cmdToggleReadingMode(requestId?: string): BridgeResponse {
    const executed = asKiokuApp(this.app).commands.executeCommandById("markdown:toggle-preview");
    if (!executed) {
      return {
        requestId,
        success: false,
        error: "Could not toggle reading mode. Make sure a Markdown note is active.",
      };
    }
    return { requestId, success: true, data: { mode: "toggled" } };
  }

  private cmdGetSelection(requestId?: string): BridgeResponse {
    const markdownView = this.app.workspace.getActiveViewOfType(MarkdownView);
    if (!markdownView) {
      return { requestId, success: true, data: { selection: null } };
    }

    const selection = markdownView.editor.getSelection();
    return {
      requestId,
      success: true,
      data: {
        selection: selection || null,
        hasSelection: selection.length > 0,
        length: selection.length,
      },
    };
  }

  private cmdFoldAll(requestId?: string): BridgeResponse {
    const executed = asKiokuApp(this.app).commands.executeCommandById("editor:fold-all");
    if (!executed) {
      return {
        requestId,
        success: false,
        error: "Could not fold headings. Make sure a Markdown note is open in editing mode.",
      };
    }
    return { requestId, success: true, data: { action: "fold-all" } };
  }

  private cmdUnfoldAll(requestId?: string): BridgeResponse {
    const executed = asKiokuApp(this.app).commands.executeCommandById("editor:unfold-all");
    if (!executed) {
      return {
        requestId,
        success: false,
        error: "Could not unfold headings. Make sure a Markdown note is open in editing mode.",
      };
    }
    return { requestId, success: true, data: { action: "unfold-all" } };
  }

  private cmdReloadSnippets(requestId?: string): BridgeResponse {
    const executed = asKiokuApp(this.app).commands.executeCommandById("app:reload-css-snippets");
    if (!executed) {
      return {
        requestId,
        success: false,
        error:
          "Could not reload CSS snippets. The command 'app:reload-css-snippets' was not available.",
      };
    }
    return { requestId, success: true, data: { action: "reload-snippets" } };
  }

  private cmdInsertAtCursor(payload: { text: string }, requestId?: string): BridgeResponse {
    const { text } = payload;
    const markdownView = this.app.workspace.getActiveViewOfType(MarkdownView);
    if (!markdownView) {
      return { requestId, success: false, error: "No active Markdown note" };
    }
    markdownView.editor.replaceRange(text, markdownView.editor.getCursor());
    return { requestId, success: true, data: { action: "insert-at-cursor" } };
  }

  private cmdReplaceSelection(payload: { text: string }, requestId?: string): BridgeResponse {
    const { text } = payload;
    const markdownView = this.app.workspace.getActiveViewOfType(MarkdownView);
    if (!markdownView) {
      return { requestId, success: false, error: "No active Markdown note" };
    }
    markdownView.editor.replaceSelection(text);
    return { requestId, success: true, data: { action: "replace-selection" } };
  }

  private async cmdCreateNoteUi(
    payload: { path: string },
    requestId?: string
  ): Promise<BridgeResponse> {
    const { path } = payload;
    try {
      const existing = this.app.vault.getFileByPath(path);
      const file = existing ?? (await this.app.vault.create(path, ""));
      const leaf = this.app.workspace.getLeaf(false);
      await leaf.openFile(file);
      return { requestId, success: true, data: { path } };
    } catch (err) {
      return { requestId, success: false, error: String(err) };
    }
  }

  private cmdScrollToBlock(payload: { blockId: string }, requestId?: string): BridgeResponse {
    const { blockId } = payload;
    const markdownView = this.app.workspace.getActiveViewOfType(MarkdownView);
    if (!markdownView) {
      return { requestId, success: false, error: "No active Markdown note" };
    }
    const editor = markdownView.editor;
    const lineCount = editor.lineCount();
    for (let i = 0; i < lineCount; i++) {
      const line = editor.getLine(i);
      if (line.includes(`^${blockId}`)) {
        editor.scrollIntoView({ from: { line: i, ch: 0 }, to: { line: i, ch: line.length } }, true);
        editor.setCursor({ line: i, ch: 0 });
        return { requestId, success: true, data: { blockId, line: i } };
      }
    }
    return {
      requestId,
      success: false,
      error: `Block ID '^${blockId}' not found in the active note`,
    };
  }

  private async cmdOpenInSplit(
    payload: { path: string },
    requestId?: string
  ): Promise<BridgeResponse> {
    const { path } = payload;
    const file = this.app.vault.getFileByPath(path);
    if (!file) {
      return { requestId, success: false, error: `File not found: ${path}` };
    }
    const leaf = this.app.workspace.getLeaf("split");
    await leaf.openFile(file);
    return { requestId, success: true, data: { path } };
  }

  // Command implementations — Plugin Integrations

  private async cmdRunDataviewQuery(
    payload: { query: string },
    requestId?: string
  ): Promise<BridgeResponse> {
    const dvApi = asKiokuApp(this.app).plugins.plugins.dataview?.api;
    if (!dvApi) {
      return {
        requestId,
        success: false,
        error: "Dataview plugin is not enabled or installed.",
      };
    }
    try {
      const result = await dvApi.query(payload.query);
      return { requestId, success: true, data: result };
    } catch (err) {
      return { requestId, success: false, error: `Dataview query error: ${String(err)}` };
    }
  }

  private async cmdRunTemplater(
    payload: { templatePath: string; targetNote?: string },
    requestId?: string
  ): Promise<BridgeResponse> {
    const templater = asKiokuApp(this.app).plugins.plugins["templater-obsidian"];
    if (!templater) {
      return {
        requestId,
        success: false,
        error: "Templater plugin is not enabled or installed.",
      };
    }
    try {
      const file = this.app.vault.getFileByPath(payload.templatePath);
      if (!file) {
        return {
          requestId,
          success: false,
          error: `Template file not found: ${payload.templatePath}`,
        };
      }
      const targetFile = payload.targetNote
        ? this.app.vault.getFileByPath(payload.targetNote)
        : this.app.workspace.getActiveFile();
      if (!targetFile) {
        return { requestId, success: false, error: "No target note specified and no active note." };
      }
      const parent = targetFile.parent;
      if (!parent) {
        return { requestId, success: false, error: "Target note has no parent folder." };
      }
      await templater.templater.create_new_note_from_template(file, parent, file.basename);
      return {
        requestId,
        success: true,
        data: { template: payload.templatePath, target: targetFile.path },
      };
    } catch (err) {
      return { requestId, success: false, error: `Templater error: ${String(err)}` };
    }
  }

  private cmdRunLinter(payload: { notePath?: string }, requestId?: string): BridgeResponse {
    const file = payload.notePath
      ? this.app.vault.getFileByPath(payload.notePath)
      : this.app.workspace.getActiveFile();
    if (!file) {
      return { requestId, success: false, error: "No note specified and no active note." };
    }
    const cmdId = "obsidian-linter:lint-file";
    const executed = asKiokuApp(this.app).commands.executeCommandById(cmdId);
    if (!executed) {
      return {
        requestId,
        success: false,
        error: "Linter plugin not found or command unavailable.",
      };
    }
    return { requestId, success: true, data: { note: file.path, command: cmdId } };
  }

  private cmdRunLinterVault(requestId?: string): BridgeResponse {
    const cmdId = "obsidian-linter:lint-all-files";
    const executed = asKiokuApp(this.app).commands.executeCommandById(cmdId);
    if (!executed) {
      return {
        requestId,
        success: false,
        error:
          "Linter 'lint all files' command not found. Ensure obsidian-linter is enabled and up to date.",
      };
    }
    return { requestId, success: true, data: { action: "lint-vault", command: cmdId } };
  }

  private cmdGetInstalledPlugins(requestId?: string): BridgeResponse {
    const app = asKiokuApp(this.app);
    const manifests = app.plugins.manifests;
    const enabledPlugins = app.plugins.enabledPlugins ?? new Set<string>();
    const plugins = Object.entries(manifests).map(([id, manifest]: [string, PluginManifest]) => ({
      id,
      name: manifest.name,
      version: manifest.version,
      author: manifest.author,
      description: manifest.description,
      enabled: enabledPlugins.has(id),
    }));
    return { requestId, success: true, data: plugins };
  }

  // Configuration

  async loadSettings() {
    this.settings = Object.assign({}, DEFAULT_SETTINGS, await this.loadData());
  }

  async saveSettings() {
    await this.saveData(this.settings);
  }
}

// Configuration Settings Tab

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
