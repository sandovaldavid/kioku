#!/usr/bin/env node
import { spawn } from "node:child_process";
import { readFileSync } from "node:fs";
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const args = process.argv.slice(2);
const mode = args.includes("--write") ? "write" : args.includes("--check") ? "check" : null;
if (!mode || (args.includes("--write") && args.includes("--check"))) {
  fail("Usage: node scripts/generate-public-docs.mjs (--write|--check) [--server-command <path>] [--server-arg <arg> ...]");
}

const option = (name) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : null;
};
const repeated = (name) => args.flatMap((value, index) => value === name ? [args[index + 1]] : []).filter(Boolean);
const serverCommand = option("--server-command") ?? "dotnet";
const serverArgs = option("--server-command")
  ? repeated("--server-arg")
  : ["run", "--project", "src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj", "--configuration", "Release", "--no-build", "--"];

async function main() {
  const metadataPath = path.join(root, "docs", "public-metadata.json");
  const metadata = JSON.parse(await readFile(metadataPath, "utf8"));
  validateMetadata(metadata);
  await validateRepositoryMetadata(metadata);

  const allCapabilities = [...metadata.capabilities.enabledByDefault, ...metadata.capabilities.disabledByDefault];
  const defaultContract = await inspectProfile("default", null);
  const completeContract = await inspectProfile("all-capabilities", allCapabilities);
  const generated = new Map([
    [path.join(root, "docs", "commands-reference.md"), renderCommands(defaultContract, completeContract, metadata)],
    [path.join(root, "docs", "configuration-reference.md"), renderConfiguration(metadata)],
    [path.join(root, "docs", "versioning.md"), renderVersioning(metadata)],
    [path.join(root, "src", "Kioku.Mcp.Server", ".mcp", "server.json"), renderManifest(metadata)],
  ]);

  for (const [file, content] of generated) {
    await applyGeneratedFile(file, content);
  }

  await validateTerminology();
  console.log(`[ok] Public documentation metadata is ${mode === "write" ? "generated" : "in sync"}.`);
  console.log(`[ok] Default profile: ${defaultContract.tools.length} tools; all capabilities: ${completeContract.tools.length} tools.`);
}

async function inspectProfile(profileName, capabilities) {
  const vault = await mkdtemp(path.join(tmpdir(), `kioku-docs-${profileName}-`));
  try {
    await writeFile(path.join(vault, "seed.md"), "# Documentation probe\n", "utf8");
    if (capabilities) {
      await mkdir(path.join(vault, ".kioku"), { recursive: true });
      const enabled = capabilities.join(", ");
      await writeFile(path.join(vault, ".kioku", "config.yml"), `capabilities:\n  require_explicit: true\n  enabled: [${enabled}]\n`, "utf8");
    }

    const client = new JsonLineMcpClient(serverCommand, serverArgs, {
      ...process.env,
      KIOKU_VAULT_PATH: vault,
      KIOKU_TRANSPORT: "stdio",
      KIOKU_OLLAMA_URL: "http://127.0.0.1:9",
      KIOKU_GEN_MODEL: "",
    });
    try {
      await client.start();
      await client.request("initialize", {
        protocolVersion: "2025-06-18",
        capabilities: {},
        clientInfo: { name: "kioku-doc-generator", version: "1" },
      });
      client.notify("notifications/initialized", {});
      const [tools, prompts, resources, templates] = await Promise.all([
        client.request("tools/list", {}),
        client.request("prompts/list", {}),
        client.request("resources/list", {}),
        client.request("resources/templates/list", {}),
      ]);
      return {
        tools: tools.tools ?? [],
        prompts: prompts.prompts ?? [],
        resources: resources.resources ?? [],
        resourceTemplates: templates.resourceTemplates ?? [],
      };
    } finally {
      await client.stop();
    }
  } finally {
    await rm(vault, { recursive: true, force: true });
  }
}

class JsonLineMcpClient {
  constructor(command, commandArgs, env) {
    this.command = command;
    this.commandArgs = commandArgs;
    this.env = env;
    this.nextId = 1;
    this.pending = new Map();
    this.buffer = "";
  }

  async start() {
    this.child = spawn(this.command, this.commandArgs, {
      cwd: root,
      env: this.env,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    this.child.stdout.setEncoding("utf8");
    this.child.stderr.setEncoding("utf8");
    this.stderr = "";
    this.child.stderr.on("data", (chunk) => { this.stderr = (this.stderr + chunk).slice(-12000); });
    this.child.stdout.on("data", (chunk) => this.consume(chunk));
    this.child.on("exit", (code, signal) => {
      const error = new Error(`Kioku documentation probe exited (${code ?? signal}).\n${this.stderr}`);
      for (const { reject } of this.pending.values()) reject(error);
      this.pending.clear();
    });
    await new Promise((resolve, reject) => {
      this.child.once("spawn", resolve);
      this.child.once("error", reject);
    });
  }

  consume(chunk) {
    this.buffer += chunk;
    while (true) {
      const newline = this.buffer.indexOf("\n");
      if (newline < 0) return;
      const line = this.buffer.slice(0, newline).trim();
      this.buffer = this.buffer.slice(newline + 1);
      if (!line) continue;
      let message;
      try { message = JSON.parse(line); } catch { continue; }
      if (message.id === undefined) continue;
      const pending = this.pending.get(message.id);
      if (!pending) continue;
      this.pending.delete(message.id);
      clearTimeout(pending.timer);
      if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
      else pending.resolve(message.result);
    }
  }

  request(method, params) {
    const id = this.nextId++;
    const payload = { jsonrpc: "2.0", id, method, params };
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}.\n${this.stderr}`));
      }, 30000);
      this.pending.set(id, { resolve, reject, timer });
      this.child.stdin.write(`${JSON.stringify(payload)}\n`);
    });
  }

  notify(method, params) {
    this.child.stdin.write(`${JSON.stringify({ jsonrpc: "2.0", method, params })}\n`);
  }

  async stop() {
    if (!this.child || this.child.killed) return;
    this.child.kill("SIGKILL");
    await new Promise((resolve) => this.child.once("exit", resolve));
  }
}

function renderCommands(defaultContract, completeContract, metadata) {
  const defaultNames = new Set(defaultContract.tools.map((tool) => tool.name));
  const tools = [...completeContract.tools].sort((a, b) => a.name.localeCompare(b.name));
  const prompts = [...completeContract.prompts].sort((a, b) => a.name.localeCompare(b.name));
  const resources = [...completeContract.resources].sort((a, b) => a.uri.localeCompare(b.uri));
  const templates = [...completeContract.resourceTemplates].sort((a, b) => a.uriTemplate.localeCompare(b.uriTemplate));
  const lines = [
    "# MCP Contract Reference",
    "",
    "> Generated from live MCP discovery. Do not edit manually.",
    "> Regenerate: `node scripts/generate-public-docs.mjs --write`",
    "> Verify: `node scripts/generate-public-docs.mjs --check`",
    "",
    "## Profiles",
    "",
    `- Default profile: **${defaultContract.tools.length} tools**.`,
    `- All-capabilities profile: **${tools.length} tools**.`,
    `- Prompts: **${prompts.length}**; direct resources: **${resources.length}**; resource templates: **${templates.length}**.`,
    "",
    `Enabled by default: ${metadata.capabilities.enabledByDefault.map(code).join(", ")}.`,
    "",
    `Disabled by default: ${metadata.capabilities.disabledByDefault.map(code).join(", ")}.`,
    "",
    "## Tools",
    "",
    "`*` marks required fields. Schemas and behavioral annotations come directly from MCP discovery.",
    "",
    "| Tool | Profile | Input schema | Output schema | Behavioral annotations |",
    "|---|---|---|---|---|",
  ];
  for (const tool of tools) {
    const annotations = tool.annotations ?? {};
    const behavior = [
      `readOnly=${annotations.readOnlyHint ?? false}`,
      `destructive=${annotations.destructiveHint ?? false}`,
      `idempotent=${annotations.idempotentHint ?? false}`,
      `openWorld=${annotations.openWorldHint ?? false}`,
    ].join("; ");
    lines.push(`| \`${escapeCell(tool.name)}\` | ${defaultNames.has(tool.name) ? "default" : "optional"} | ${escapeCell(schemaSummary(tool.inputSchema))} | ${escapeCell(schemaSummary(tool.outputSchema))} | ${behavior} |`);
  }
  lines.push("", "## Prompts", "", "| Prompt | Arguments |", "|---|---|");
  for (const prompt of prompts) {
    const promptArgs = (prompt.arguments ?? []).map((argument) => `${argument.name}${argument.required ? "*" : ""}`).join("; ") || "—";
    lines.push(`| \`${escapeCell(prompt.name)}\` | ${escapeCell(promptArgs)} |`);
  }
  lines.push("", "## Resources", "", "| URI | Kind |", "|---|---|");
  for (const resource of resources) lines.push(`| \`${escapeCell(resource.uri)}\` | direct |`);
  for (const template of templates) lines.push(`| \`${escapeCell(template.uriTemplate)}\` | template |`);
  return `${lines.join("\n").trim()}\n`;
}

function schemaSummary(schema) {
  if (!schema || typeof schema !== "object") return "—";
  const properties = schema.properties ?? {};
  const required = new Set(schema.required ?? []);
  const fields = Object.entries(properties)
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([name, property]) => `${name}:${schemaType(property)}${required.has(name) ? "*" : ""}`);
  return fields.length ? fields.join("; ") : schema.type ?? "object";
}

function schemaType(schema) {
  if (Array.isArray(schema?.type)) return schema.type.join("|");
  if (schema?.type === "array") return `array<${schemaType(schema.items ?? {})}>`;
  if (schema?.type) return schema.type;
  if (schema?.anyOf) return schema.anyOf.map(schemaType).join("|");
  if (schema?.oneOf) return schema.oneOf.map(schemaType).join("|");
  if (schema?.$ref) return schema.$ref.split("/").at(-1);
  return "object";
}

function renderConfiguration(metadata) {
  const lines = [
    "# Server Configuration Reference",
    "",
    "> Generated from `docs/public-metadata.json`. Do not edit manually.",
    "> Regenerate: `node scripts/generate-public-docs.mjs --write`",
    "> Verify: `node scripts/generate-public-docs.mjs --check`",
    "",
    "The `Kioku` configuration section is canonical. `KIOKU_*` environment variables remain supported compatibility aliases and are mechanically checked against runtime mappings and the MCP package manifest.",
    "",
  ];
  const categories = [...new Set(metadata.environmentVariables.map((item) => item.category))];
  for (const category of categories) {
    lines.push(`## ${category}`, "", "| Environment variable | Configuration path | Required | Sensitive | Default | Description |", "|---|---|---:|---:|---|---|");
    for (const item of metadata.environmentVariables.filter((entry) => entry.category === category)) {
      lines.push(`| \`${item.name}\` | \`${item.configurationPath}\` | ${item.required ? "yes" : "no"} | ${item.sensitive ? "yes" : "no"} | ${item.default === null ? "—" : `\`${escapeCell(item.default)}\``} | ${escapeCell(item.description)} |`);
    }
    lines.push("");
  }
  lines.push("## Transport terminology", "");
  for (const transport of metadata.transports) lines.push(`- \`${transport.configurationValue}\` — **${transport.publicName}**: ${transport.description}`);
  return `${lines.join("\n").trim()}\n`;
}

function renderVersioning(metadata) {
  const version = readServerVersionSync();
  return `# Versioning Policy\n\n> Generated from \`docs/public-metadata.json\`. Do not edit manually.\n> Verify: \`node scripts/generate-public-docs.mjs --check\`\n\n## Server\n\nCurrent server package version: **${version}**. ${metadata.versioning.server}\n\n## Obsidian plugin\n\n${metadata.versioning.plugin}\n\n## Root workspace\n\n${metadata.versioning.rootWorkspace}\n\n## Bridge compatibility\n\n${metadata.versioning.bridge}\n`;
}

function renderManifest(metadata) {
  const version = readServerVersionSync();
  const manifest = {
    $schema: "https://static.modelcontextprotocol.io/schemas/2025-10-17/server.schema.json",
    description: metadata.server.description,
    name: metadata.server.manifestName,
    version,
    packages: [{
      registryType: "nuget",
      identifier: metadata.server.packageId,
      version,
      transport: { type: "stdio" },
      packageArguments: [],
      environmentVariables: metadata.environmentVariables.map(({ name, description }) => ({ name, description })),
    }],
    repository: { url: metadata.server.repository, source: "github" },
  };
  return `${JSON.stringify(manifest, null, 2)}\n`;
}

async function validateRepositoryMetadata(metadata) {
  const names = metadata.environmentVariables.map((item) => item.name);
  assertUnique(names, "environment variable");
  const optionsSource = await readFile(path.join(root, "src", "Kioku.Mcp.Server", "Hosting", "KiokuOptions.cs"), "utf8");
  const runtimeNames = new Set([...optionsSource.matchAll(/KIOKU_[A-Z0-9_]+/g)].map((match) => match[0]));
  assertSameSet(new Set(names), runtimeNames, "public metadata", "KiokuOptions runtime mappings");

  const rootPackage = JSON.parse(await readFile(path.join(root, "package.json"), "utf8"));
  if (rootPackage.private !== true) fail("Root package.json must remain private.");
  if (Object.hasOwn(rootPackage, "version")) fail("Private root package.json must not declare a product version.");
}

async function validateTerminology() {
  const roots = ["README.md", "docs", "src/Kioku.Mcp.Server/README.md"];
  const excluded = new Set(["CHANGELOG.md", "docs/migration-v3.md"]);
  const patterns = [/HTTP[-+ ]SSE/iu, /HTTP\s*\/\s*SSE/iu];
  for (const entry of roots) {
    const absolute = path.join(root, entry);
    for (const file of await enumerateTextFiles(absolute)) {
      const relative = path.relative(root, file).replaceAll("\\", "/");
      if (excluded.has(relative)) continue;
      const text = await readFile(file, "utf8");
      if (patterns.some((pattern) => pattern.test(text))) fail(`Legacy HTTP+SSE terminology remains in ${relative}.`);
    }
  }
}

async function enumerateTextFiles(target) {
  const stat = await import("node:fs/promises").then((fs) => fs.stat(target));
  if (stat.isFile()) return [target];
  const files = [];
  for (const entry of await readdir(target, { withFileTypes: true })) {
    if (["bin", "obj", "node_modules", ".git"].includes(entry.name)) continue;
    const child = path.join(target, entry.name);
    if (entry.isDirectory()) files.push(...await enumerateTextFiles(child));
    else if (/\.(md|html|json|ya?ml|service|txt)$/iu.test(entry.name)) files.push(child);
  }
  return files;
}

async function applyGeneratedFile(file, content) {
  if (mode === "write") {
    await mkdir(path.dirname(file), { recursive: true });
    await writeFile(file, content, "utf8");
    console.log(`[write] ${path.relative(root, file)}`);
    return;
  }
  let existing = "";
  try { existing = await readFile(file, "utf8"); } catch { fail(`Generated file is missing: ${path.relative(root, file)}`); }
  if (normalize(existing) !== normalize(content)) fail(`Generated file is stale: ${path.relative(root, file)}. Run node scripts/generate-public-docs.mjs --write.`);
}

function readServerVersionSync() {
  const project = readFileSync(path.join(root, "src", "Kioku.Mcp.Server", "Kioku.Mcp.Server.csproj"), "utf8");
  const match = project.match(/<PackageVersion>([^<]+)<\/PackageVersion>/u);
  if (!match) fail("Kioku.Mcp.Server.csproj does not declare PackageVersion.");
  return match[1].trim();
}

function validateMetadata(value) {
  if (value.schemaVersion !== 1) fail("Unsupported public metadata schemaVersion.");
  if (!Array.isArray(value.environmentVariables) || !value.environmentVariables.length) fail("environmentVariables must be non-empty.");
}
function assertUnique(values, label) {
  if (new Set(values).size !== values.length) fail(`Duplicate ${label} metadata detected.`);
}
function assertSameSet(expected, actual, expectedLabel, actualLabel) {
  const missing = [...expected].filter((value) => !actual.has(value));
  const extra = [...actual].filter((value) => !expected.has(value));
  if (missing.length || extra.length) fail(`${expectedLabel} and ${actualLabel} differ. Missing: ${missing.join(", ") || "none"}. Extra: ${extra.join(", ") || "none"}.`);
}
function normalize(value) { return value.replaceAll("\r\n", "\n").trimEnd(); }
function code(value) { return `\`${value}\``; }
function escapeCell(value) { return String(value).replaceAll("|", "\\|").replaceAll("\n", " "); }
function fail(message) { console.error(`[error] ${message}`); process.exit(1); }

await main();
