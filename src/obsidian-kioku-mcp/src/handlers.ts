import { timingSafeEqual } from "node:crypto";
import { MarkdownView, Notice } from "obsidian";
import type { App } from "obsidian";
import type { BridgeResponse, KiokuSettings, PluginManifest } from "./types";
import {
  executeObsidianCommand,
  getObsidianVersion,
  getThirdPartyPluginApi,
  getVaultBasePath,
  listInstalledPlugins,
} from "./obsidian-compat";

function validatePayload(
  payload: Record<string, unknown> | undefined,
  requiredFields: string[],
  requestId?: string
): BridgeResponse | null {
  if (!payload) {
    return {
      requestId,
      success: false,
      error: `Missing payload. Required fields: ${requiredFields.join(", ")}`,
    };
  }

  const missing = requiredFields.filter((field) => {
    const value = payload[field];
    return value === undefined || value === null || value === "";
  });

  if (missing.length > 0) {
    return {
      requestId,
      success: false,
      error: `Missing required field(s): ${missing.join(", ")}`,
    };
  }

  return null;
}

export function createHandlers(app: App, settings: KiokuSettings, pluginManifest: PluginManifest) {
  return {
    auth: (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdAuth(settings, p, requestId),
    "open-file": async (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["path"], requestId);
      if (validation) return validation;
      return cmdOpenFile(app, settings, p as { path: string }, requestId);
    },
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
      cmdGetAppVersion(app, pluginManifest, requestId),
    "get-open-notes": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetOpenNotes(app, requestId),
    "trigger-command": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["commandId"], requestId);
      if (validation) return validation;
      return cmdTriggerCommand(app, p as { commandId: string }, requestId);
    },
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
    "insert-at-cursor": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["text"], requestId);
      if (validation) return validation;
      return cmdInsertAtCursor(app, p as { text: string }, requestId);
    },
    "replace-selection": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["text"], requestId);
      if (validation) return validation;
      return cmdReplaceSelection(app, p as { text: string }, requestId);
    },
    "create-note-ui": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["path"], requestId);
      if (validation) return validation;
      return cmdCreateNoteUi(app, p as { path: string }, requestId);
    },
    "scroll-to-block": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["blockId"], requestId);
      if (validation) return validation;
      return cmdScrollToBlock(app, p as { blockId: string }, requestId);
    },
    "open-in-split": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["path"], requestId);
      if (validation) return validation;
      return cmdOpenInSplit(app, p as { path: string }, requestId);
    },
    "run-dataview-query": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["query"], requestId);
      if (validation) return validation;
      return cmdRunDataviewQuery(app, p as { query: string }, requestId);
    },
    "run-templater": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["templatePath"], requestId);
      if (validation) return validation;
      return cmdRunTemplater(app, p as { templatePath: string; targetNote?: string }, requestId);
    },
    "evaluate-templater-in-file": (p: Record<string, unknown> | undefined, requestId?: string) => {
      const validation = validatePayload(p, ["notePath"], requestId);
      if (validation) return validation;
      return cmdEvaluateTemplaterInFile(app, p as { notePath: string }, requestId);
    },
    "run-linter": (p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunLinter(app, p as { notePath?: string }, requestId),
    "run-linter-vault": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdRunLinterVault(app, requestId),
    "get-installed-plugins": (_p: Record<string, unknown> | undefined, requestId?: string) =>
      cmdGetInstalledPlugins(app, requestId),
  };
}

function cmdAuth(
  settings: KiokuSettings,
  payload: Record<string, unknown> | undefined,
  requestId?: string
): BridgeResponse {
  if (!settings.authToken) {
    return { requestId, success: true, data: { authenticated: true } };
  }

  const token = typeof payload?.token === "string" ? payload.token : undefined;
  if (token && isTokenValid(settings.authToken, token)) {
    return { requestId, success: true, data: { authenticated: true } };
  }

  return {
    requestId,
    success: false,
    error: "[error] [UNAUTHORIZED] Invalid or missing authentication token.",
  };
}

function isTokenValid(expected: string, actual: string): boolean {
  const expectedBuf = Buffer.from(expected, "utf8");
  const actualBuf = Buffer.from(actual, "utf8");
  if (expectedBuf.length !== actualBuf.length) {
    return false;
  }
  return timingSafeEqual(expectedBuf, actualBuf);
}

async function cmdOpenFile(
  app: App,
  settings: KiokuSettings,
  payload: { path: string },
  requestId?: string
): Promise<BridgeResponse> {
  const { path } = payload;
  const file = app.vault.getFileByPath(path);

  if (!file) {
    return { requestId, success: false, error: `File not found: ${path}` };
  }

  try {
    await app.workspace.openLinkText(path, "", false);

    if (settings.showNotifications) {
      new Notice(`Kioku opened: ${file.basename}`);
    }

    return { requestId, success: true, data: { path, name: file.basename } };
  } catch (err) {
    return { requestId, success: false, error: `Failed to open file: ${String(err)}` };
  }
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
  const vaultPath = getVaultBasePath(app);
  return {
    requestId,
    success: true,
    data: {
      vaultPath: vaultPath ?? "unknown",
      vaultName: app.vault.getName(),
      available: vaultPath !== null,
    },
  };
}

function cmdGetAppVersion(
  app: App,
  pluginManifest: PluginManifest,
  requestId?: string
): BridgeResponse {
  return {
    requestId,
    success: true,
    data: {
      obsidianVersion: getObsidianVersion(app) ?? "unknown",
      kiokuVersion: pluginManifest.version,
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
  const executed = executeObsidianCommand(app, commandId);
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
  const executed = executeObsidianCommand(app, "markdown:toggle-preview");
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
  const executed = executeObsidianCommand(app, "editor:fold-all");
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
  const executed = executeObsidianCommand(app, "editor:unfold-all");
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
  const executed = executeObsidianCommand(app, "app:reload-css-snippets");
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
  const dvApi = getThirdPartyPluginApi(app, "dataview") as
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
  const templater = getThirdPartyPluginApi(app, "templater-obsidian") as
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

async function cmdEvaluateTemplaterInFile(
  app: App,
  payload: { notePath: string },
  requestId?: string
): Promise<BridgeResponse> {
  const templater = getThirdPartyPluginApi(app, "templater-obsidian") as
    | {
        templater: {
          overwrite_file_commands: (file: unknown, activeFile?: boolean) => Promise<void>;
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
    const file = app.vault.getFileByPath(payload.notePath);
    if (!file) {
      return { requestId, success: false, error: `Note not found: ${payload.notePath}` };
    }
    await templater.templater.overwrite_file_commands(file);
    return { requestId, success: true, data: { path: file.path } };
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
  const executed = executeObsidianCommand(app, cmdId);
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
  const executed = executeObsidianCommand(app, cmdId);
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
  return {
    requestId,
    success: true,
    data: listInstalledPlugins(app),
  };
}
