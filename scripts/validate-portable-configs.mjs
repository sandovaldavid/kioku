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

async function validateIntegrationMetadata() {
  try {
    const serverProject = await readFile(
      path.join(rootDir, "src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj"),
      "utf8",
    );
    const commandsReference = await readFile(path.join(rootDir, "docs/commands-reference.md"), "utf8");
    const claudePlugin = JSON.parse(
      await readFile(
        path.join(rootDir, "integrations/claude-code-plugin/.claude-plugin/plugin.json"),
        "utf8",
      ),
    );
    const claudeMcp = JSON.parse(
      await readFile(path.join(rootDir, "integrations/claude-code-plugin/.mcp.json"), "utf8"),
    );
    const marketplace = JSON.parse(
      await readFile(path.join(rootDir, ".claude-plugin/marketplace.json"), "utf8"),
    );
    const antigravityRules = await readFile(
      path.join(rootDir, "integrations/antigravity-plugin/rules/kioku.md"),
      "utf8",
    );
    const vaultSkill = await readFile(
      path.join(rootDir, "integrations/claude-code-plugin/skills/kioku-vault/SKILL.md"),
      "utf8",
    );
    const projectWorkflowSkill = await readFile(
      path.join(rootDir, "integrations/claude-code-plugin/skills/kioku-project-workflow/SKILL.md"),
      "utf8",
    );

    const packageVersionMatch = serverProject.match(/<PackageVersion>([^<]+)<\/PackageVersion>/);
    const defaultToolsMatch = commandsReference.match(/Default profile: \*\*(\d+) tools\*\*\./);
    const allToolsMatch = commandsReference.match(/All-capabilities profile: \*\*(\d+) tools\*\*\./);
    const disabledDefaultsMatch = commandsReference.match(/Disabled by default:\s*([^\n]+)/);

    if (!packageVersionMatch || !defaultToolsMatch || !allToolsMatch || !disabledDefaultsMatch) {
      failures.push("integration metadata: unable to derive canonical version/capability metadata");
      return;
    }

    const packageVersion = packageVersionMatch[1];
    const defaultTools = defaultToolsMatch[1];
    const allTools = allToolsMatch[1];
    const disabledGroups = [...disabledDefaultsMatch[1].matchAll(/`([^`]+)`/g)].map((match) => match[1]);

    if (claudePlugin.version !== packageVersion) {
      failures.push(
        `integrations/claude-code-plugin/.claude-plugin/plugin.json: version '${claudePlugin.version}' must match server PackageVersion '${packageVersion}'`,
      );
    }

    const claudeServer = claudeMcp.mcpServers?.kioku;
    if (!claudeServer || claudeServer.type !== "stdio" || claudeServer.command !== "kioku") {
      failures.push(
        "integrations/claude-code-plugin/.mcp.json: kioku must remain a stdio server launched with command 'kioku'",
      );
    }

    const claudeVaultPath = claudeServer?.env?.KIOKU_VAULT_PATH;
    if (claudeVaultPath !== "${KIOKU_VAULT_PATH}") {
      failures.push(
        `integrations/claude-code-plugin/.mcp.json: KIOKU_VAULT_PATH must be '\${KIOKU_VAULT_PATH}', found '${claudeVaultPath}'`,
      );
    }

    if (Object.hasOwn(claudePlugin.userConfig ?? {}, "vault_path")) {
      failures.push(
        "integrations/claude-code-plugin/.claude-plugin/plugin.json: userConfig.vault_path must remain machine-local",
      );
    }

    const pluginDescription = claudePlugin.description ?? "";
    if (!pluginDescription.includes(`${defaultTools} default tools`)) {
      failures.push(
        `integrations/claude-code-plugin/.claude-plugin/plugin.json: description must advertise ${defaultTools} default tools`,
      );
    }
    if (!pluginDescription.includes(`${allTools} all-capabilities tools`)) {
      failures.push(
        `integrations/claude-code-plugin/.claude-plugin/plugin.json: description must advertise ${allTools} all-capabilities tools`,
      );
    }

    for (const group of disabledGroups) {
      if (!antigravityRules.includes(`\`${group}\``)) {
        failures.push(
          `integrations/antigravity-plugin/rules/kioku.md: missing disabled-by-default capability '${group}'`,
        );
      }
      if (!vaultSkill.includes(`\`${group}\``)) {
        failures.push(
          `integrations/claude-code-plugin/skills/kioku-vault/SKILL.md: missing disabled-by-default capability '${group}'`,
        );
      }
    }

    if (!vaultSkill.includes(`${defaultTools}-tool default profile`)) {
      failures.push(
        `integrations/claude-code-plugin/skills/kioku-vault/SKILL.md: description must advertise the ${defaultTools}-tool default profile`,
      );
    }
    if (!vaultSkill.includes(`${allTools}-tool all-capabilities profile`)) {
      failures.push(
        `integrations/claude-code-plugin/skills/kioku-vault/SKILL.md: description must advertise the ${allTools}-tool all-capabilities profile`,
      );
    }

    for (const tool of [
      "create_engineering_spec",
      "create_implementation_plan",
      "get_project_context",
      "start_work_session",
      "end_work_session",
    ]) {
      if (!projectWorkflowSkill.includes(`\`${tool}\``)) {
        failures.push(
          `integrations/claude-code-plugin/skills/kioku-project-workflow/SKILL.md: missing current workflow tool '${tool}'`,
        );
      }
    }

    const marketplaceEntry = marketplace.plugins?.find((plugin) => plugin.name === "kioku");
    const marketplaceDescription = marketplaceEntry?.description ?? "";
    for (const skill of ["kioku-vault", "kioku-project-workflow"]) {
      if (!marketplaceDescription.includes(skill)) {
        failures.push(`.claude-plugin/marketplace.json: Kioku description must mention '${skill}'`);
      }
    }

    if (marketplaceEntry?.source !== "./integrations/claude-code-plugin") {
      failures.push(
        ".claude-plugin/marketplace.json: Kioku source must point to './integrations/claude-code-plugin'",
      );
    }

    if (Object.hasOwn(marketplaceEntry ?? {}, "version")) {
      failures.push(
        ".claude-plugin/marketplace.json: do not duplicate the plugin version; plugin.json is the single version authority",
      );
    }
  } catch (error) {
    failures.push(`integration metadata: validation failed (${error.message})`);
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
    const kiokuServer = agConfig.mcpServers?.kioku;
    if (!kiokuServer || kiokuServer.command !== "kioku") {
      failures.push(`integrations/antigravity-plugin/mcp_config.json: mcpServers.kioku command must be 'kioku'`);
    }
  } catch (error) {
    failures.push(`integrations/antigravity-plugin/mcp_config.json: invalid or missing configuration (${error.message})`);
  }

  await validateIntegrationMetadata();

  if (failures.length > 0) {
    console.error("[error] Portable configuration validation failed:");
    for (const failure of failures) {
      console.error(`- ${failure}`);
    }
    process.exit(1);
  }

  console.log("[ok] Portable configurations and integration metadata are consistent.");
}

validateConfigs();
