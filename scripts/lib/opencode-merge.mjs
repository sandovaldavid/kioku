#!/usr/bin/env node
// Dependency-free helper that writes/merges the "kioku" MCP server entry into an
// OpenCode config file (opencode.json or opencode.jsonc).
//
// Usage: node opencode-merge.mjs <target-file> <vault-path>
//
// - If the target file doesn't exist, writes a fresh minimal config.
// - If it exists and parses as plain JSON, merges in `mcp.kioku` and rewrites the file.
// - If it exists but fails to parse (most commonly because it's JSONC with comments),
//   the file is left untouched: the snippet to paste manually is printed to stdout and
//   the process exits with code 2 so the caller can tell "please paste this" apart from
//   a hard failure (exit 1).

import { existsSync, readFileSync, writeFileSync, mkdirSync } from "node:fs";
import { dirname } from "node:path";

const [, , targetFile, vaultPath] = process.argv;

if (!targetFile || !vaultPath) {
  console.error("Usage: opencode-merge.mjs <target-file> <vault-path>");
  process.exit(1);
}

const kiokuEntry = {
  type: "local",
  command: ["kioku"],
  environment: {
    KIOKU_VAULT_PATH: vaultPath,
  },
  enabled: true,
};

function printManualSnippet() {
  console.log(JSON.stringify({ mcp: { kioku: kiokuEntry } }, null, 2));
}

if (!existsSync(targetFile)) {
  const fresh = {
    $schema: "https://opencode.ai/config.json",
    mcp: {
      kioku: kiokuEntry,
    },
  };
  mkdirSync(dirname(targetFile), { recursive: true });
  writeFileSync(targetFile, JSON.stringify(fresh, null, 2) + "\n", "utf8");
  console.log(`Created ${targetFile} with the kioku MCP server.`);
  process.exit(0);
}

const raw = readFileSync(targetFile, "utf8");
let parsed;
try {
  parsed = JSON.parse(raw);
} catch {
  console.error(
    `Could not parse ${targetFile} as plain JSON (it may contain comments). ` +
      "Leaving it untouched. Add this manually under the top-level \"mcp\" key:",
  );
  printManualSnippet();
  process.exit(2);
}

parsed.mcp = parsed.mcp || {};
parsed.mcp.kioku = kiokuEntry;
writeFileSync(targetFile, JSON.stringify(parsed, null, 2) + "\n", "utf8");
console.log(`Updated ${targetFile} with the kioku MCP server.`);
