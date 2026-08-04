#!/usr/bin/env node

import { access, readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const docsRoot = path.join(root, "docs");
const layoutPath = path.join(docsRoot, "_layouts", "default.html");
const configPath = path.join(docsRoot, "_config.yml");

const layout = await readFile(layoutPath, "utf8");
const config = await readFile(configPath, "utf8");
const failures = [];

const sidebarTargets = [...layout.matchAll(/href="\{\{ site\.baseurl \}\}\/([^"#?]+\.html)" class="sidebar-link/gu)]
  .map((match) => match[1])
  .filter((target, index, values) => values.indexOf(target) === index)
  .sort();

if (sidebarTargets.length === 0) {
  failures.push("docs/_layouts/default.html: no sidebar destinations were discovered");
}

for (const htmlTarget of sidebarTargets) {
  const markdownTarget = htmlTarget.replace(/\.html$/u, ".md");
  const file = path.join(docsRoot, markdownTarget);

  try {
    await access(file);
  } catch {
    failures.push(`docs/_layouts/default.html: sidebar destination is missing: docs/${markdownTarget}`);
    continue;
  }

  const markdown = await readFile(file, "utf8");
  const frontmatter = readFrontmatter(markdown);
  const defaults = readExactDefaults(config, markdownTarget);
  const effective = { ...defaults, ...frontmatter };

  if (effective.layout !== "default") {
    failures.push(`docs/${markdownTarget}: sidebar destination must use layout: default`);
  }
  if (effective.sidebar !== "true") {
    failures.push(`docs/${markdownTarget}: sidebar destination must set sidebar: true directly or through docs/_config.yml defaults`);
  }
  if (!effective.title) {
    failures.push(`docs/${markdownTarget}: sidebar destination must define a title directly or through docs/_config.yml defaults`);
  }
}

if (failures.length > 0) {
  console.error("[error] Documentation sidebar contract is invalid:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`[ok] Documentation sidebar contract is valid for ${sidebarTargets.length} destinations.`);

function readFrontmatter(markdown) {
  if (!markdown.startsWith("---\n") && !markdown.startsWith("---\r\n")) return {};
  const normalized = markdown.replaceAll("\r\n", "\n");
  const end = normalized.indexOf("\n---\n", 4);
  if (end < 0) return {};
  return parseSimpleMapping(normalized.slice(4, end));
}

function readExactDefaults(yaml, targetPath) {
  const normalized = yaml.replaceAll("\r\n", "\n");
  const escaped = escapeRegExp(targetPath);
  const block = normalized.match(new RegExp(
    `(?:^|\\n)  - scope:\\n      path: ["']${escaped}["']\\n    values:\\n((?:      [^\\n]+\\n?)*)`,
    "u",
  ));
  if (!block) return {};
  return parseSimpleMapping(block[1].replace(/^      /gmu, ""));
}

function parseSimpleMapping(source) {
  const values = {};
  for (const line of source.split("\n")) {
    const match = line.match(/^([a-zA-Z0-9_-]+):\s*(.*?)\s*$/u);
    if (!match) continue;
    values[match[1]] = unquote(match[2]);
  }
  return values;
}

function unquote(value) {
  if ((value.startsWith('"') && value.endsWith('"')) ||
      (value.startsWith("'") && value.endsWith("'"))) {
    return value.slice(1, -1);
  }
  return value;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&");
}
