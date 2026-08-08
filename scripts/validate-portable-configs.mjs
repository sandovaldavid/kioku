#!/usr/bin/env node

import { readFile, readdir, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const rootDir = path.resolve(scriptDir, "..");
const failures = [];

// Maintainer personal path patterns that should NEVER appear in configuration or integration assets
const forbiddenPatterns = [
  /\/home\/sandovaldavid/i,
  /Cortex-L7/i,
];

// Target configuration files and directories to inspect
const targetFiles = [
  ".mcp.json",
  "opencode.json",
];

const targetDirectories = [
  "integrations",
];

async function scanFile(filePath) {
  const relativePath = path.relative(rootDir, filePath);
  try {
    const content = await readFile(filePath, "utf8");
    for (const pattern of forbiddenPatterns) {
      if (pattern.test(content)) {
        failures.push(`${relativePath}: contains forbidden maintainer path pattern matching '${pattern}'`);
      }
    }
  } catch (error) {
    failures.push(`${relativePath}: failed to read file (${error.message})`);
  }
}

async function scanDirectory(dirPath) {
  const entries = await readdir(dirPath, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(dirPath, entry.name);
    if (entry.isDirectory()) {
      await scanDirectory(fullPath);
    } else if (entry.isFile()) {
      await scanFile(fullPath);
    }
  }
}

async function validateConfigs() {
  for (const relativeFile of targetFiles) {
    const fullPath = path.join(rootDir, relativeFile);
    await scanFile(fullPath);
  }

  for (const relativeDir of targetDirectories) {
    const fullPath = path.join(rootDir, relativeDir);
    const stats = await stat(fullPath).catch(() => null);
    if (stats && stats.isDirectory()) {
      await scanDirectory(fullPath);
    }
  }

  // Verify .mcp.json resolves vault from environment
  try {
    const mcpJson = JSON.parse(await readFile(path.join(rootDir, ".mcp.json"), "utf8"));
    const vaultPath = mcpJson.mcpServers?.kioku?.env?.KIOKU_VAULT_PATH;
    if (vaultPath !== "${KIOKU_VAULT_PATH}") {
      failures.push(`.mcp.json: KIOKU_VAULT_PATH must be '\${KIOKU_VAULT_PATH}', found '${vaultPath}'`);
    }
  } catch (error) {
    failures.push(`.mcp.json: invalid or missing configuration (${error.message})`);
  }

  // Verify opencode.json resolves vault from environment
  try {
    const opencodeJson = JSON.parse(await readFile(path.join(rootDir, "opencode.json"), "utf8"));
    const vaultPath = opencodeJson.mcp?.kioku?.environment?.KIOKU_VAULT_PATH;
    if (vaultPath !== "{env:KIOKU_VAULT_PATH}") {
      failures.push(`opencode.json: KIOKU_VAULT_PATH must be '{env:KIOKU_VAULT_PATH}', found '${vaultPath}'`);
    }
  } catch (error) {
    failures.push(`opencode.json: invalid or missing configuration (${error.message})`);
  }

  // Verify antigravity plugin mcp_config.json
  try {
    const agConfig = JSON.parse(await readFile(path.join(rootDir, "integrations/antigravity-plugin/mcp_config.json"), "utf8"));
    const vaultPath = agConfig.mcpServers?.kioku?.env?.KIOKU_VAULT_PATH;
    if (vaultPath !== "${KIOKU_VAULT_PATH}") {
      failures.push(`integrations/antigravity-plugin/mcp_config.json: KIOKU_VAULT_PATH must be '\${KIOKU_VAULT_PATH}', found '${vaultPath}'`);
    }
  } catch (error) {
    failures.push(`integrations/antigravity-plugin/mcp_config.json: invalid or missing configuration (${error.message})`);
  }

  if (failures.length > 0) {
    console.error("[error] Portable configuration validation failed:");
    for (const failure of failures) {
      console.error(`- ${failure}`);
    }
    process.exit(1);
  }

  console.log("[ok] Portable configurations and integration assets contain no maintainer paths.");
}

validateConfigs();
