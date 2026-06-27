import { MarkdownView, Notice } from "obsidian";
import type { App } from "obsidian";
import type { BridgeResponse, KiokuDataAdapter, KiokuSettings, PluginManifest } from "./types";
import { asKiokuApp } from "./types";

export function createHandlers(app: App, settings: KiokuSettings) {
  return {
    "open-file": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdOpenFile(app, settings, p as { path: string }, requestId),
    "get-active-note": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetActiveNote(app, requestId),
    "get-vault-path": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetVaultPath(app, requestId),
    "is-obsidian-ready": (_p: Record<string, unknown> | undefined, requestId?: string) => ({
      success: true,
      data: { ready: true },
      requestId,
    }),
    "get-app-version": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetAppVersion(app, requestId),
    "get-open-notes": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetOpenNotes(app, requestId),
    "trigger-command": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdTriggerCommand(app, p as { commandId: string }, requestId),
    "toggle-reading-mode": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdToggleReadingMode(app, requestId),
    "get-selection": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetSelection(app, requestId),
    "fold-all-headings": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdFoldAll(app, requestId),
    "unfold-all-headings": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdUnfoldAll(app, requestId),
    "reload-snippets": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdReloadSnippets(app, requestId),
    "insert-at-cursor": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdInsertAtCursor(app, p as { text: string }, requestId),
    "replace-selection": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdReplaceSelection(app, p as { text: string }, requestId),
    "create-note-ui": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdCreateNoteUi(app, p as { path: string }, requestId),
    "scroll-to-block": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdScrollToBlock(app, p as { blockId: string }, requestId),
    "open-in-split": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdOpenInSplit(app, p as { path: string }, requestId),
    "run-dataview-query": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunDataviewQuery(app, p as { query: string }, requestId),
    "run-templater": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunTemplater(app, p as { templatePath: string; targetNote?: string }, requestId),
    "run-linter": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunLinter(app, p as { notePath?: string }, requestId),
    "run-linter-vault": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunLinterVault(app, requestId),
    "get-installed-plugins": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetInstalledPlugins(app, requestId),
  };
}

function cmdOpenFile(
  app: App,
  settings: KiokuSettings,
  payload: { path: string },
  requestId?: string
): BridgeResponse {
  const { path } = payload;
  const file = app.vault.getFileByPath(path);

  if (!file) {
    return { requestId, success: false, error: `File not found: ${path}` };
  }

  void app.workspace.openLinkText(path, "", false);

  if (settings.showNotifications) {
    new Notice(`Kioku opened: ${file.basename}`);
  }

  return { requestId, success: true, data: { path, name: file.basename } };
}

function cmdGetActiveNote(app: App, requestId?: string): BridgeResponse {
  const activeFile = app.workspace.getActiveFile();

  if (!activeFile) {
    return { requestId, success: true, data: null };
  }

  const cache = app.metadataCache.getFileCache(activeFile);
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

function cmdGetVaultPath(app: App, requestId?: string): BridgeResponse {
  // Note: vault.adapter is an internal Obsidian API. We cast to KiokuDataAdapter
  // to access basePath. If this API changes, we return "unknown" as fallback.
  const adapter = app.vault.adapter as unknown as KiokuDataAdapter;
  const vaultPath = adapter?.basePath ?? "unknown";

  return {
    requestId,
    success: true,
    data: { vaultPath, vaultName: app.vault.getName() },
  };
}

function cmdGetAppVersion(app: App, requestId?: string): BridgeResponse {
  const kiokuApp = asKiokuApp(app);
  const kiokuManifest = kiokuApp.plugins.manifests["kioku-mcp"];
  return {
    requestId,
    success: true,
    data: {
      obsidianVersion: kiokuApp.version,
      kiokuVersion: kiokuManifest?.version ?? "unknown",
    },
  };
}

function cmdGetOpenNotes(app: App, requestId?: string): BridgeResponse {
  const openFiles: Array<{ path: string; name: string }> = [];

  app.workspace.iterateAllLeaves((leaf) => {
    if (leaf.view instanceof MarkdownView) {
      const file = leaf.view.file;
      if (file) {
        openFiles.push({ path: file.path, name: file.basename });
      }
    }
  });

  return { requestId, success: true, data: openFiles };
}

function cmdTriggerCommand(
  app: App,
  payload: { commandId: string },
  requestId?: string
): BridgeResponse {
  const { commandId } = payload;
  const executed = asKiokuApp(app).commands.executeCommandById(commandId);
  if (!executed) {
    return {
      requestId,
      success: false,
      error: `Command not found or not executable: '${commandId}'`,
    };
  }
  return { requestId, success: true, data: { commandId } };
}

function cmdToggleReadingMode(app: App, requestId?: string): BridgeResponse {
  const executed = asKiokuApp(app).commands.executeCommandById("markdown:toggle-preview");
  if (!executed) {
    return {
      requestId,
      success: false,
      error: "Could not toggle reading mode. Make sure a Markdown note is active.",
    };
  }
  return { requestId, success: true, data: { mode: "toggled" } };
}

function cmdGetSelection(app: App, requestId?: string): BridgeResponse {
  const markdownView = app.workspace.getActiveViewOfType(MarkdownView);
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

function cmdFoldAll(app: App, requestId?: string): BridgeResponse {
  const executed = asKiokuApp(app).commands.executeCommandById("editor:fold-all");
  if (!executed) {
    return {
      requestId,
      success: false,
      error: "Could not fold headings. Make sure a Markdown note is open in editing mode.",
    };
  }
  return { requestId, success: true, data: { action: "fold-all" } };
}

function cmdUnfoldAll(app: App, requestId?: string): BridgeResponse {
  const executed = asKiokuApp(app).commands.executeCommandById("editor:unfold-all");
  if (!executed) {
    return {
      requestId,
      success: false,
      error: "Could not unfold headings. Make sure a Markdown note is open in editing mode.",
    };
  }
  return { requestId, success: true, data: { action: "unfold-all" } };
}

function cmdReloadSnippets(app: App, requestId?: string): BridgeResponse {
  const executed = asKiokuApp(app).commands.executeCommandById("app:reload-css-snippets");
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

function cmdInsertAtCursor(
  app: App,
  payload: { text: string },
  requestId?: string
): BridgeResponse {
  const { text } = payload;
  const markdownView = app.workspace.getActiveViewOfType(MarkdownView);
  if (!markdownView) {
    return { requestId, success: false, error: "No active Markdown note" };
  }
  markdownView.editor.replaceRange(text, markdownView.editor.getCursor());
  return { requestId, success: true, data: { action: "insert-at-cursor" } };
}

function cmdReplaceSelection(
  app: App,
  payload: { text: string },
  requestId?: string
): BridgeResponse {
  const { text } = payload;
  const markdownView = app.workspace.getActiveViewOfType(MarkdownView);
  if (!markdownView) {
    return { requestId, success: false, error: "No active Markdown note" };
  }
  markdownView.editor.replaceSelection(text);
  return { requestId, success: true, data: { action: "replace-selection" } };
}

async function cmdCreateNoteUi(
  app: App,
  payload: { path: string },
  requestId?: string
): Promise<BridgeResponse> {
  const { path } = payload;
  try {
    const existing = app.vault.getFileByPath(path);
    const file = existing ?? (await app.vault.create(path, ""));
    const leaf = app.workspace.getLeaf(false);
    await leaf.openFile(file);
    return { requestId, success: true, data: { path } };
  } catch (err) {
    return { requestId, success: false, error: String(err) };
  }
}

function cmdScrollToBlock(
  app: App,
  payload: { blockId: string },
  requestId?: string
): BridgeResponse {
  const { blockId } = payload;
  const markdownView = app.workspace.getActiveViewOfType(MarkdownView);
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

async function cmdOpenInSplit(
  app: App,
  payload: { path: string },
  requestId?: string
): Promise<BridgeResponse> {
  const { path } = payload;
  const file = app.vault.getFileByPath(path);
  if (!file) {
    return { requestId, success: false, error: `File not found: ${path}` };
  }
  const leaf = app.workspace.getLeaf("split");
  await leaf.openFile(file);
  return { requestId, success: true, data: { path } };
}

async function cmdRunDataviewQuery(
  app: App,
  payload: { query: string },
  requestId?: string
): Promise<BridgeResponse> {
  const dvApi = asKiokuApp(app).plugins.plugins.dataview as
    | { api: { query: (q: string) => Promise<unknown> } }
    | undefined;
  if (!dvApi) {
    return {
      requestId,
      success: false,
      error: "Dataview plugin is not enabled or installed.",
    };
  }
  try {
    const result = await dvApi.api.query(payload.query);
    return { requestId, success: true, data: result };
  } catch (err) {
    return { requestId, success: false, error: `Dataview query error: ${String(err)}` };
  }
}

async function cmdRunTemplater(
  app: App,
  payload: { templatePath: string; targetNote?: string },
  requestId?: string
): Promise<BridgeResponse> {
  const plugins = asKiokuApp(app).plugins.plugins;
  const templater = plugins["templater-obsidian"] as
    | {
        templater: {
          create_new_note_from_template: (f: unknown, p: unknown, n: string) => Promise<void>;
        };
      }
    | undefined;
  if (!templater) {
    return {
      requestId,
      success: false,
      error: "Templater plugin is not enabled or installed.",
    };
  }
  try {
    const file = app.vault.getFileByPath(payload.templatePath);
    if (!file) {
      return {
        requestId,
        success: false,
        error: `Template file not found: ${payload.templatePath}`,
      };
    }
    const targetFile = payload.targetNote
      ? app.vault.getFileByPath(payload.targetNote)
      : app.workspace.getActiveFile();
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

function cmdRunLinter(
  app: App,
  payload: { notePath?: string },
  requestId?: string
): BridgeResponse {
  const file = payload.notePath
    ? app.vault.getFileByPath(payload.notePath)
    : app.workspace.getActiveFile();
  if (!file) {
    return { requestId, success: false, error: "No note specified and no active note." };
  }
  const cmdId = "obsidian-linter:lint-file";
  const executed = asKiokuApp(app).commands.executeCommandById(cmdId);
  if (!executed) {
    return {
      requestId,
      success: false,
      error: "Linter plugin not found or command unavailable.",
    };
  }
  return { requestId, success: true, data: { note: file.path, command: cmdId } };
}

function cmdRunLinterVault(app: App, requestId?: string): BridgeResponse {
  const cmdId = "obsidian-linter:lint-all-files";
  const executed = asKiokuApp(app).commands.executeCommandById(cmdId);
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

function cmdGetInstalledPlugins(app: App, requestId?: string): BridgeResponse {
  const kiokuApp = asKiokuApp(app);
  const manifests = kiokuApp.plugins.manifests;
  const enabledPlugins = kiokuApp.plugins.enabledPlugins ?? new Set<string>();
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
