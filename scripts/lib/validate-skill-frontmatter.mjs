#!/usr/bin/env node
// Validates SKILL.md frontmatter for every skill under integrations/. Checks the
// constraints shared by Claude Code and OpenCode's Claude-compatible skill loader:
// `name` must match the containing directory, be lowercase-kebab-case, and
// `description` must be present and <= 1024 chars.

import { readFileSync, readdirSync, statSync } from "node:fs";
import { join, basename, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT_DIR = join(dirname(fileURLToPath(import.meta.url)), "..", "..");
const NAME_RE = /^[a-z0-9]+(-[a-z0-9]+)*$/;

function findSkillFiles(dir, out = []) {
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    const stat = statSync(full);
    if (stat.isDirectory()) {
      findSkillFiles(full, out);
    } else if (entry === "SKILL.md") {
      out.push(full);
    }
  }
  return out;
}

function parseFrontmatter(text) {
  const match = text.match(/^---\n([\s\S]*?)\n---/);
  if (!match) return null;
  const fields = {};
  for (const line of match[1].split("\n")) {
    const m = line.match(/^([a-zA-Z0-9_]+):\s*(.*)$/);
    if (m) fields[m[1]] = m[2].trim();
  }
  return fields;
}

const skillFiles = findSkillFiles(join(ROOT_DIR, "integrations"));
if (skillFiles.length === 0) {
  console.error("No SKILL.md files found under integrations/ — expected at least one.");
  process.exit(1);
}

let failed = false;

for (const file of skillFiles) {
  const dirName = basename(dirname(file));
  const text = readFileSync(file, "utf8");
  const fm = parseFrontmatter(text);
  let fileFailed = false;

  if (!fm) {
    console.error(`${file}: missing YAML frontmatter`);
    failed = true;
    continue;
  }

  const { name, description } = fm;

  if (!name) {
    console.error(`${file}: frontmatter is missing "name"`);
    fileFailed = true;
  } else {
    if (name !== dirName) {
      console.error(`${file}: name "${name}" does not match directory "${dirName}"`);
      fileFailed = true;
    }
    if (!NAME_RE.test(name) || name.length > 64) {
      console.error(`${file}: name "${name}" must be lowercase-kebab-case, <= 64 chars`);
      fileFailed = true;
    }
  }

  if (!description) {
    console.error(`${file}: frontmatter is missing "description"`);
    fileFailed = true;
  } else if (description.length > 1024) {
    console.error(`${file}: description exceeds 1024 chars (${description.length})`);
    fileFailed = true;
  }

  if (fileFailed) {
    failed = true;
  } else {
    console.log(`OK: ${file}`);
  }
}

process.exit(failed ? 1 : 0);
