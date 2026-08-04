#!/usr/bin/env node

import { access, readdir, readFile, stat } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const roots = [
  "AGENTS.md",
  "README.md",
  "CONTRIBUTING.md",
  "CLAUDE.md",
  "SECURITY.md",
  "docs",
  "integrations/README.md",
  "scripts/Kioku.Ci/README.md",
  "src/Kioku.Mcp.Server/README.md",
];

const failures = [];
for (const entry of roots) {
  const absolute = path.join(root, entry);
  await access(absolute);
  for (const file of await enumerateMarkdownFiles(absolute)) {
    await validateFile(file);
  }
}

if (failures.length > 0) {
  console.error("[error] Broken repository-relative Markdown links:");
  for (const failure of [...new Set(failures)].sort()) {
    console.error(`- ${failure}`);
  }
  process.exit(1);
}

console.log("[ok] Repository-relative Markdown links are valid.");

async function validateFile(file) {
  const relativeFile = toRepositoryPath(file);
  const markdown = stripNonLinkContent(await readFile(file, "utf8"));

  for (const rawTarget of extractTargets(markdown)) {
    const target = normalizeTarget(rawTarget);
    if (shouldIgnore(target)) {
      continue;
    }

    const pathPart = target.split("#", 1)[0].split("?", 1)[0];
    if (!pathPart) {
      continue;
    }

    let decoded;
    try {
      decoded = decodeURIComponent(pathPart);
    } catch {
      failures.push(`${relativeFile}: invalid URI encoding in ${JSON.stringify(rawTarget)}`);
      continue;
    }

    const candidate = path.resolve(path.dirname(file), decoded);
    const relativeTarget = path.relative(root, candidate);
    if (relativeTarget === ".." ||
        relativeTarget.startsWith(`..${path.sep}`) ||
        path.isAbsolute(relativeTarget)) {
      failures.push(`${relativeFile}: link escapes the repository: ${JSON.stringify(rawTarget)}`);
      continue;
    }

    try {
      await stat(candidate);
    } catch {
      failures.push(`${relativeFile}: missing ${toRepositoryPath(candidate)} from ${JSON.stringify(rawTarget)}`);
    }
  }
}

async function enumerateMarkdownFiles(target) {
  const targetStat = await stat(target);
  if (targetStat.isFile()) {
    return target.endsWith(".md") ? [target] : [];
  }

  const files = [];
  for (const entry of await readdir(target, { withFileTypes: true })) {
    if ([".git", "bin", "node_modules", "obj"].includes(entry.name)) {
      continue;
    }

    const child = path.join(target, entry.name);
    if (entry.isDirectory()) {
      files.push(...await enumerateMarkdownFiles(child));
    } else if (entry.isFile() && entry.name.endsWith(".md")) {
      files.push(child);
    }
  }

  return files;
}

function extractTargets(markdown) {
  const targets = [];
  for (const match of markdown.matchAll(/!?\[[^\]]*\]\(([^)\n]+)\)/gu)) {
    targets.push(match[1]);
  }
  for (const match of markdown.matchAll(/^\s*\[[^\]]+\]:\s*(\S+)/gmu)) {
    targets.push(match[1]);
  }
  return targets;
}

function normalizeTarget(rawTarget) {
  const target = rawTarget.trim();
  if (target.startsWith("<")) {
    const closing = target.indexOf(">");
    return closing >= 0 ? target.slice(1, closing).trim() : target;
  }

  return target.match(/^\S+/u)?.[0] ?? "";
}

function shouldIgnore(target) {
  return !target ||
    target.startsWith("#") ||
    target.startsWith("/") ||
    target.startsWith("//") ||
    /^[a-z][a-z0-9+.-]*:/iu.test(target);
}

function stripNonLinkContent(markdown) {
  let withoutComments = markdown;
  let previous;
  do {
    previous = withoutComments;
    withoutComments = withoutComments.replace(/<!--[\s\S]*?-->/gu, "");
  } while (withoutComments !== previous);
  const lines = withoutComments.split("\n");
  let fenceCharacter = null;
  let fenceLength = 0;

  return lines.map((line) => {
    const fence = line.match(/^\s*(`{3,}|~{3,})/u)?.[1];
    if (fence) {
      if (fenceCharacter === null) {
        fenceCharacter = fence[0];
        fenceLength = fence.length;
        return "";
      }
      if (fence[0] === fenceCharacter && fence.length >= fenceLength) {
        fenceCharacter = null;
        fenceLength = 0;
        return "";
      }
    }

    return fenceCharacter === null
      ? line.replace(/`+[^`\n]*`+/gu, "")
      : "";
  }).join("\n");
}

function toRepositoryPath(value) {
  return path.relative(root, value).replaceAll("\\", "/");
}
