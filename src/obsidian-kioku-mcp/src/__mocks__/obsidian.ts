// Minimal Obsidian mocks for Vitest unit tests.
// These mocks only implement the surface area used by src/handlers.ts.

export class Notice {
  message: string;
  constructor(message: string) {
    this.message = message;
  }
}

export class TFile {
  path: string;
  basename: string;
  parent: TFolder | null;

  constructor(path: string, parent: TFolder | null = null) {
    this.path = path;
    this.basename = path.split("/").pop() ?? path;
    this.parent = parent;
  }
}

export class TFolder {
  path: string;

  constructor(path: string) {
    this.path = path;
  }
}

export class MarkdownView {
  file: TFile | null = null;
  editor: Editor;

  constructor(editor: Editor) {
    this.editor = editor;
  }
}

export interface Editor {
  getSelection(): string;
  getCursor(): { line: number; ch: number };
  replaceRange(text: string, from: { line: number; ch: number }): void;
  replaceSelection(text: string): void;
  getLine(line: number): string;
  lineCount(): number;
  scrollIntoView(
    range: { from: { line: number; ch: number }; to: { line: number; ch: number } },
    center: boolean
  ): void;
  setCursor(cursor: { line: number; ch: number }): void;
}

export interface Vault {
  adapter: { basePath?: string };
  getFileByPath(path: string): TFile | null;
  getName(): string;
  create(path: string, content: string): Promise<TFile>;
}

export interface Workspace {
  getActiveFile(): TFile | null;
  getActiveViewOfType<T>(type: new (...args: unknown[]) => T): T | null;
  getLeaf(type?: string | boolean): WorkspaceLeaf;
  iterateAllLeaves(callback: (leaf: WorkspaceLeaf) => void): void;
  openLinkText(linktext: string, sourcePath: string, newTab: boolean): Promise<void>;
}

export interface WorkspaceLeaf {
  view: unknown;
  openFile(file: TFile): Promise<void>;
}

export interface App {
  vault: Vault;
  workspace: Workspace;
  metadataCache: {
    getFileCache(file: TFile): { frontmatter?: { tags?: string[]; status?: string } } | null;
  };
}

export interface PluginManifest {
  name: string;
  version: string;
  author?: string;
  description?: string;
}
