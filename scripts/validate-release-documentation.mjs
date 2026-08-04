#!/usr/bin/env node

import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(scriptDir, "..");
const failures = [];

const read = async (relativePath) => readFile(path.join(root, relativePath), "utf8");

const projectPath = "src/Kioku.Mcp.Server/Kioku.Mcp.Server.csproj";
const manifestPath = "src/Kioku.Mcp.Server/.mcp/server.json";
const releaseManifestPath = ".release-please-manifest.json";
const releaseConfigPath = "release-please-config.json";

const [
  project,
  manifestText,
  releaseManifestText,
  releaseConfigText,
  rootReadme,
  packageReadme,
  agents,
  docsConfig,
  versioning,
] = await Promise.all([
  read(projectPath),
  read(manifestPath),
  read(releaseManifestPath),
  read(releaseConfigPath),
  read("README.md"),
  read("src/Kioku.Mcp.Server/README.md"),
  read("AGENTS.md"),
  read("docs/_config.yml"),
  read("docs/versioning.md"),
]);

const versionMatch = project.match(/<PackageVersion>([^<]+)<\/PackageVersion>/u);
if (!versionMatch) fail(`${projectPath}: PackageVersion is missing`);
const version = versionMatch[1].trim();

expect(project, /<PackageReadmeFile>README\.md<\/PackageReadmeFile>/u,
  `${projectPath}: PackageReadmeFile must remain README.md`);
expect(project, /<None Include="README\.md" Pack="true" PackagePath="\/"\s*\/>/u,
  `${projectPath}: the NuGet README must be packed at the package root`);

const manifest = parseJson(manifestText, manifestPath);
if (manifest.version !== version) {
  failures.push(`${manifestPath}: version ${manifest.version ?? "missing"} does not match ${version}`);
}
const packageVersion = manifest.packages?.[0]?.version;
if (packageVersion !== version) {
  failures.push(`${manifestPath}: packages[0].version ${packageVersion ?? "missing"} does not match ${version}`);
}

const releaseManifest = parseJson(releaseManifestText, releaseManifestPath);
if (releaseManifest["."] !== version) {
  failures.push(`${releaseManifestPath}: package version ${releaseManifest["."] ?? "missing"} does not match ${version}`);
}

for (const [relativePath, source] of [
  ["README.md", rootReadme],
  ["src/Kioku.Mcp.Server/README.md", packageReadme],
  ["AGENTS.md", agents],
  ["docs/_config.yml", docsConfig],
  ["docs/versioning.md", versioning],
]) {
  validateVersionMarker(relativePath, source, version);
}

const releaseConfig = parseJson(releaseConfigText, releaseConfigPath);
const extraFiles = releaseConfig.packages?.["."]?.["extra-files"];
if (!Array.isArray(extraFiles)) {
  failures.push(`${releaseConfigPath}: packages["."].extra-files must be an array`);
} else {
  const requiredGenericPaths = [
    "README.md",
    "AGENTS.md",
    "src/Kioku.Mcp.Server/README.md",
    "docs/_config.yml",
    "docs/versioning.md",
  ];
  for (const requiredPath of requiredGenericPaths) {
    if (!extraFiles.some((entry) => entry.type === "generic" && entry.path === requiredPath)) {
      failures.push(`${releaseConfigPath}: missing generic release update for ${requiredPath}`);
    }
  }

  if (!extraFiles.some((entry) =>
    entry.type === "xml" &&
    entry.path === projectPath &&
    entry.xpath === "//PackageVersion")) {
    failures.push(`${releaseConfigPath}: missing PackageVersion XML update for ${projectPath}`);
  }

  for (const jsonpath of ["$.version", "$.packages[0].version"]) {
    if (!extraFiles.some((entry) =>
      entry.type === "json" &&
      entry.path === manifestPath &&
      entry.jsonpath === jsonpath)) {
      failures.push(`${releaseConfigPath}: missing ${jsonpath} update for ${manifestPath}`);
    }
  }
}

if (failures.length > 0) {
  console.error("[error] Release-facing documentation is inconsistent:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`[ok] Release-facing documentation matches server package version ${version}.`);

function validateVersionMarker(relativePath, source, expectedVersion) {
  const markerLine = source
    .replaceAll("\r\n", "\n")
    .split("\n")
    .find((line) => line.includes("x-release-please-version"));

  if (!markerLine) {
    failures.push(`${relativePath}: x-release-please-version marker is missing`);
    return;
  }

  const markedVersion = markerLine.match(/\bv?(\d+\.\d+\.\d+)\b/u)?.[1];
  if (markedVersion !== expectedVersion) {
    failures.push(`${relativePath}: marked version ${markedVersion ?? "missing"} does not match ${expectedVersion}`);
  }
}

function expect(source, pattern, message) {
  if (!pattern.test(source)) failures.push(message);
}

function parseJson(source, relativePath) {
  try {
    return JSON.parse(source);
  } catch (error) {
    fail(`${relativePath}: invalid JSON (${error.message})`);
  }
}

function fail(message) {
  console.error(`[error] ${message}`);
  process.exit(1);
}
