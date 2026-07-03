import { describe, it, expect, vi } from "vitest";
import { createHandlers } from "./handlers";
import { createMockApp, createMockFile, createMockEditor } from "./test-utils";
import type { App } from "obsidian";

vi.mock("obsidian", () => import("./__mocks__/obsidian"));

const manifest = {
  name: "Kioku",
  version: "1.8.0-beta.5",
  author: "David Sandoval",
  description: "Obsidian plugin bridge for Kioku MCP Server",
};

const settings = { bridgePort: 7765, showNotifications: false, showStatusBar: true, authToken: "" };

function makeApp(options: Parameters<typeof createMockApp>[0] = {}) {
  return createMockApp(options) as unknown as App;
}

describe("createHandlers", () => {
  describe("open-file", () => {
    it("returns success:false when path is missing", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["open-file"](undefined, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("Missing payload. Required fields: path");
      expect(result.requestId).toBe("req-1");
    });

    it("returns success:false when file is not found", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["open-file"]({ path: "missing.md" }, "req-2");
      expect(result.success).toBe(false);
      expect(result.error).toBe("File not found: missing.md");
      expect(result.requestId).toBe("req-2");
    });

    it("returns success:true when file exists", async () => {
      const file = createMockFile("Projects/Kioku.md");
      const handlers = createHandlers(
        makeApp({ files: [file], activeView: { file, editor: createMockEditor() } }),
        settings,
        manifest
      );
      const result = await handlers["open-file"]({ path: "Projects/Kioku.md" }, "req-3");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ path: "Projects/Kioku.md", name: "Kioku" });
      expect(result.requestId).toBe("req-3");
    });

    it("returns success:false when openLinkText throws", async () => {
      const file = createMockFile("Projects/Kioku.md");
      const app = makeApp({ files: [file] });
      app.workspace.openLinkText = vi.fn().mockRejectedValue(new Error("boom"));
      const handlers = createHandlers(app, settings, manifest);
      const result = await handlers["open-file"]({ path: "Projects/Kioku.md" }, "req-4");
      expect(result.success).toBe(false);
      expect(result.error).toContain("boom");
    });
  });

  describe("get-active-note", () => {
    it("returns null when no active file", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["get-active-note"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toBeNull();
    });

    it("returns active note metadata", () => {
      const file = createMockFile("Inbox/Idea.md");
      const metadataCache = new Map([
        ["Inbox/Idea.md", { frontmatter: { tags: ["idea"], status: "draft" } }],
      ]);
      const handlers = createHandlers(
        makeApp({ activeFile: file, metadataCache }),
        settings,
        manifest
      );
      const result = handlers["get-active-note"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({
        path: "Inbox/Idea.md",
        name: "Idea",
        tags: ["idea"],
        status: "draft",
      });
    });
  });

  describe("get-vault-path", () => {
    it("returns the vault path and name", () => {
      const handlers = createHandlers(
        makeApp({ vaultPath: "/home/user/vault" }),
        settings,
        manifest
      );
      const result = handlers["get-vault-path"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ vaultPath: "/home/user/vault", vaultName: "TestVault" });
    });
  });

  describe("auth", () => {
    it("succeeds as a no-op when no token is configured", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers.auth(undefined, "req-1");
      expect(result.success).toBe(true);
    });

    it("succeeds as a no-op even with a payload when no token is configured", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers.auth({ token: "anything" }, "req-1");
      expect(result.success).toBe(true);
    });

    it("succeeds when the token matches", () => {
      const handlers = createHandlers(makeApp(), { ...settings, authToken: "s3cr3t" }, manifest);
      const result = handlers.auth({ token: "s3cr3t" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.requestId).toBe("req-1");
    });

    it("fails when the token does not match", () => {
      const handlers = createHandlers(makeApp(), { ...settings, authToken: "s3cr3t" }, manifest);
      const result = handlers.auth({ token: "wrong" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("[UNAUTHORIZED]");
    });

    it("fails when no payload is provided but a token is required", () => {
      const handlers = createHandlers(makeApp(), { ...settings, authToken: "s3cr3t" }, manifest);
      const result = handlers.auth(undefined, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("[UNAUTHORIZED]");
    });

    it("fails when the token has a different length than expected", () => {
      const handlers = createHandlers(makeApp(), { ...settings, authToken: "s3cr3t" }, manifest);
      const result = handlers.auth({ token: "s3cr3t-but-longer" }, "req-1");
      expect(result.success).toBe(false);
    });
  });

  describe("is-obsidian-ready", () => {
    it("always returns ready:true", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["is-obsidian-ready"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ ready: true });
    });
  });

  describe("get-app-version", () => {
    it("returns obsidian and kioku versions", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["get-app-version"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ obsidianVersion: "1.8.0", kiokuVersion: "1.8.0-beta.5" });
    });
  });

  describe("get-open-notes", () => {
    it("returns the list of open markdown notes", () => {
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ files: [file], openNotes: [{ path: "Notes/A.md", name: "A" }] }),
        settings,
        manifest
      );
      const result = handlers["get-open-notes"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual([{ path: "Notes/A.md", name: "A" }]);
    });
  });

  describe("trigger-command", () => {
    it("returns success:false when commandId is missing", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["trigger-command"](undefined, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("Missing payload. Required fields: commandId");
    });

    it("returns success:true when command executes", () => {
      const commands = new Map([["app:toggle-left-sidebar", true]]);
      const handlers = createHandlers(makeApp({ commands }), settings, manifest);
      const result = handlers["trigger-command"]({ commandId: "app:toggle-left-sidebar" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ commandId: "app:toggle-left-sidebar" });
    });

    it("returns success:false when command fails", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["trigger-command"]({ commandId: "unknown:cmd" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("not found or not executable");
    });
  });

  describe("toggle-reading-mode", () => {
    it("returns success:true when command is available", () => {
      const commands = new Map([["markdown:toggle-preview", true]]);
      const handlers = createHandlers(makeApp({ commands }), settings, manifest);
      const result = handlers["toggle-reading-mode"](undefined, "req-1");
      expect(result.success).toBe(true);
    });

    it("returns success:false when command is unavailable", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["toggle-reading-mode"](undefined, "req-1");
      expect(result.success).toBe(false);
    });
  });

  describe("get-selection", () => {
    it("returns null selection without markdown view", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["get-selection"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ selection: null });
    });

    it("returns the current selection", () => {
      const editor = createMockEditor({ getSelection: () => "selected text" });
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ activeView: { file, editor } }),
        settings,
        manifest
      );
      const result = handlers["get-selection"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({
        selection: "selected text",
        hasSelection: true,
        length: 13,
      });
    });
  });

  describe("insert-at-cursor", () => {
    it("returns success:false when text is missing", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["insert-at-cursor"](undefined, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("Missing payload. Required fields: text");
    });

    it("returns success:false when no markdown view is active", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["insert-at-cursor"]({ text: "hello" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toBe("No active Markdown note");
    });

    it("inserts text and returns success", () => {
      const replaceRange = vi.fn();
      const editor = createMockEditor({ replaceRange });
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ activeView: { file, editor } }),
        settings,
        manifest
      );
      const result = handlers["insert-at-cursor"]({ text: "hello" }, "req-1");
      expect(result.success).toBe(true);
      expect(replaceRange).toHaveBeenCalledWith("hello", { line: 0, ch: 0 });
    });
  });

  describe("replace-selection", () => {
    it("replaces selection and returns success", () => {
      const replaceSelection = vi.fn();
      const editor = createMockEditor({ replaceSelection });
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ activeView: { file, editor } }),
        settings,
        manifest
      );
      const result = handlers["replace-selection"]({ text: "replacement" }, "req-1");
      expect(result.success).toBe(true);
      expect(replaceSelection).toHaveBeenCalledWith("replacement");
    });
  });

  describe("create-note-ui", () => {
    it("creates and opens a new note when it does not exist", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["create-note-ui"]({ path: "New.md" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ path: "New.md" });
    });

    it("opens an existing note", async () => {
      const file = createMockFile("Existing.md");
      const handlers = createHandlers(makeApp({ files: [file] }), settings, manifest);
      const result = await handlers["create-note-ui"]({ path: "Existing.md" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ path: "Existing.md" });
    });

    it("returns success:false on error", async () => {
      const app = makeApp();
      app.vault.create = vi.fn().mockRejectedValue(new Error("cannot create"));
      const handlers = createHandlers(app, settings, manifest);
      const result = await handlers["create-note-ui"]({ path: "Bad.md" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("cannot create");
    });
  });

  describe("scroll-to-block", () => {
    it("returns success:false when blockId is missing", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["scroll-to-block"](undefined, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("Missing payload. Required fields: blockId");
    });

    it("returns success:false when no markdown view is active", () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = handlers["scroll-to-block"]({ blockId: "abc" }, "req-1");
      expect(result.success).toBe(false);
    });

    it("scrolls to block and returns line", () => {
      const scrollIntoView = vi.fn();
      const setCursor = vi.fn();
      const editor = createMockEditor({
        lineCount: () => 3,
        getLine: (line: number) => ["foo", "bar ^abc", "baz"][line] ?? "",
        scrollIntoView,
        setCursor,
      });
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ activeView: { file, editor } }),
        settings,
        manifest
      );
      const result = handlers["scroll-to-block"]({ blockId: "abc" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ blockId: "abc", line: 1 });
      expect(scrollIntoView).toHaveBeenCalled();
      expect(setCursor).toHaveBeenCalledWith({ line: 1, ch: 0 });
    });

    it("returns success:false when blockId is not found", () => {
      const editor = createMockEditor({
        lineCount: () => 1,
        getLine: () => "foo",
      });
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(
        makeApp({ activeView: { file, editor } }),
        settings,
        manifest
      );
      const result = handlers["scroll-to-block"]({ blockId: "missing" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toContain("Block ID '^missing' not found");
    });
  });

  describe("open-in-split", () => {
    it("returns success:false when file not found", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["open-in-split"]({ path: "missing.md" }, "req-1");
      expect(result.success).toBe(false);
    });

    it("returns success:true when file exists", async () => {
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(makeApp({ files: [file] }), settings, manifest);
      const result = await handlers["open-in-split"]({ path: "Notes/A.md" }, "req-1");
      expect(result.success).toBe(true);
    });
  });

  describe("run-dataview-query", () => {
    it("returns success:false when Dataview is not installed", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["run-dataview-query"]({ query: "LIST" }, "req-1");
      expect(result.success).toBe(false);
      expect(result.error).toBe("Dataview plugin is not enabled or installed.");
    });

    it("returns query result when Dataview is available", async () => {
      const app = makeApp({
        plugins: {
          plugins: {
            dataview: { api: { query: vi.fn().mockResolvedValue({ values: ["a"] }) } },
          },
        },
      });
      const handlers = createHandlers(app, settings, manifest);
      const result = await handlers["run-dataview-query"]({ query: "LIST" }, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ values: ["a"] });
    });
  });

  describe("run-templater", () => {
    it("returns success:false when Templater is not installed", async () => {
      const handlers = createHandlers(makeApp(), settings, manifest);
      const result = await handlers["run-templater"](
        { templatePath: "Templates/Daily.md" },
        "req-1"
      );
      expect(result.success).toBe(false);
      expect(result.error).toBe("Templater plugin is not enabled or installed.");
    });
  });

  describe("run-linter", () => {
    it("lints the active file", () => {
      const file = createMockFile("Notes/A.md");
      const commands = new Map([["obsidian-linter:lint-file", true]]);
      const handlers = createHandlers(makeApp({ activeFile: file, commands }), settings, manifest);
      const result = handlers["run-linter"]({}, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual({ note: "Notes/A.md", command: "obsidian-linter:lint-file" });
    });

    it("returns success:false when Linter command is unavailable", () => {
      const file = createMockFile("Notes/A.md");
      const handlers = createHandlers(makeApp({ activeFile: file }), settings, manifest);
      const result = handlers["run-linter"]({}, "req-1");
      expect(result.success).toBe(false);
    });
  });

  describe("run-linter-vault", () => {
    it("returns success:true when command is available", () => {
      const commands = new Map([["obsidian-linter:lint-all-files", true]]);
      const handlers = createHandlers(makeApp({ commands }), settings, manifest);
      const result = handlers["run-linter-vault"](undefined, "req-1");
      expect(result.success).toBe(true);
    });
  });

  describe("get-installed-plugins", () => {
    it("lists installed plugins with enabled state", () => {
      const plugins = {
        manifests: {
          kioku: {
            name: "Kioku",
            version: "1.8.0-beta.5",
            author: "David Sandoval",
            description: "Test",
          },
        },
        enabledPlugins: new Set<string>(["kioku"]),
      };
      const handlers = createHandlers(makeApp({ plugins }), settings, manifest);
      const result = handlers["get-installed-plugins"](undefined, "req-1");
      expect(result.success).toBe(true);
      expect(result.data).toEqual([
        {
          id: "kioku",
          name: "Kioku",
          version: "1.8.0-beta.5",
          author: "David Sandoval",
          description: "Test",
          enabled: true,
        },
      ]);
    });
  });
});
