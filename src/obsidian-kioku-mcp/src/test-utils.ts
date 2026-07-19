import { FileSystemAdapter, MarkdownView } from "./__mocks__/obsidian";
import type {
  App,
  Editor,
  Vault,
  Workspace,
  WorkspaceLeaf,
  TFile,
  TFolder,
} from "./__mocks__/obsidian";

export interface MockAppOptions {
  files?: TFile[];
  activeFile?: TFile | null;
  activeView?: { file: TFile | null; editor: Editor } | null;
  vaultPath?: string;
  openNotes?: Array<{ path: string; name: string }>;
  metadataCache?: Map<string, { frontmatter?: { tags?: string[]; status?: string } }>;
  commands?: Map<string, boolean>;
  plugins?: {
    manifests?: Record<
      string,
      { name: string; version: string; author?: string; description?: string }
    >;
    enabledPlugins?: Set<string>;
    plugins?: Record<string, unknown>;
  };
}

export function createMockApp(options: MockAppOptions = {}): App {
  const fileMap = new Map<string, TFile>();
  for (const file of options.files ?? []) {
    fileMap.set(file.path, file);
  }

  const metadataCache =
    options.metadataCache ??
    new Map<string, { frontmatter?: { tags?: string[]; status?: string } }>();
  const commands = options.commands ?? new Map();

  const vault: Vault = {
    adapter: new FileSystemAdapter(options.vaultPath ?? "/tmp/test-vault"),
    getFileByPath(path: string): TFile | null {
      return fileMap.get(path) ?? null;
    },
    getName(): string {
      return "TestVault";
    },
    create(path: string): Promise<TFile> {
      const file = createMockFile(path);
      fileMap.set(path, file);
      return Promise.resolve(file);
    },
  };

  const leaf: WorkspaceLeaf = {
    view: null,
    openFile(file: TFile): Promise<void> {
      this.view = { file };
      return Promise.resolve();
    },
  };

  const workspace: Workspace = {
    getActiveFile(): TFile | null {
      return options.activeFile ?? null;
    },
    getActiveViewOfType<T>(_type: new (...args: unknown[]) => T): T | null {
      if (!options.activeView) return null;
      return {
        file: options.activeView.file,
        editor: options.activeView.editor,
      } as unknown as T;
    },
    getLeaf(type?: string | boolean): WorkspaceLeaf {
      return { ...leaf, split: type === "split" } as unknown as WorkspaceLeaf;
    },
    iterateAllLeaves(callback: (leaf: WorkspaceLeaf) => void): void {
      for (const note of options.openNotes ?? []) {
        const mockFile = fileMap.get(note.path) ?? createMockFile(note.path);
        const view = new MarkdownView(createMockEditor());
        view.file = mockFile;
        callback({ view } as unknown as WorkspaceLeaf);
      }
    },
    openLinkText(): Promise<void> {
      // no-op for unit tests
      return Promise.resolve();
    },
  };

  const app = {
    vault,
    workspace,
    metadataCache: {
      getFileCache(file: TFile) {
        return metadataCache.get(file.path) ?? null;
      },
    },
  };

  // Attach the guarded internal surface exercised by obsidian-compat.ts.
  (app as unknown as Record<string, unknown>).version = "1.13.1";
  (app as unknown as Record<string, unknown>).commands = {
    executeCommandById(commandId: string): boolean {
      return (commands.get(commandId) as boolean | undefined) ?? false;
    },
  };
  (app as unknown as Record<string, unknown>).plugins = {
    manifests: options.plugins?.manifests ?? {},
    enabledPlugins: options.plugins?.enabledPlugins ?? new Set<string>(),
    plugins: options.plugins?.plugins ?? {},
  };

  return app;
}

export function createMockEditor(overrides: Partial<Editor> = {}): Editor {
  const lines: string[] = [];
  const selection = "";
  const cursor = { line: 0, ch: 0 };

  return {
    getSelection: () => selection,
    getCursor: () => cursor,
    replaceRange: () => undefined,
    replaceSelection: () => undefined,
    getLine: (line: number) => lines[line] ?? "",
    lineCount: () => lines.length,
    scrollIntoView: () => undefined,
    setCursor: () => undefined,
    ...overrides,
  };
}

export function createMockFile(path: string, parent: TFolder | null = null): TFile {
  const fullName = path.split("/").pop() ?? path;
  const basename = fullName.replace(/\.md$/, "");
  // eslint-disable-next-line @typescript-eslint/no-unnecessary-type-assertion
  return { path, basename, parent } as TFile;
}
