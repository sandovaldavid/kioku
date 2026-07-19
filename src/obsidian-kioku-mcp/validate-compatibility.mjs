import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

const sourceDirectory = "src";
const adapterPath = "src/obsidian-compat.ts";
const forbidden = [
  [".commands.executeCommandById", "command registry access"],
  [".plugins.plugins", "third-party plugin registry access"],
  [".plugins.manifests", "plugin manifest registry access"],
  [".plugins.enabledPlugins", "enabled plugin registry access"],
  ["adapter.basePath", "filesystem adapter basePath access"],
];

function files(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    return entry.isDirectory() ? files(path) : path.endsWith(".ts") ? [path] : [];
  });
}

const failures = [];
for (const path of files(sourceDirectory)) {
  if (path === adapterPath || path.includes("/__mocks__/") || path.endsWith(".test.ts")) continue;
  const content = readFileSync(path, "utf8");
  for (const [needle, label] of forbidden) {
    if (content.includes(needle)) failures.push(`${path}: direct ${label}; use obsidian-compat.ts`);
  }
}

if (failures.length > 0) {
  console.error(failures.map((failure) => `- ${failure}`).join("\n"));
  process.exit(1);
}

console.log("Obsidian internal API access is isolated behind obsidian-compat.ts.");
