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
const releaseWorkflowPath = ".github/workflows/release-please.yml";
const claudePluginPath = "integrations/claude-code-plugin/.claude-plugin/plugin.json";
const pinnedSyftVersion = "1.42.3";
const pinnedSyftSha256 = "0d6be741479eddd2c8644a288990c04f3df0d609bbc1599a005532a9dff63509";

const [
  project,
  manifestText,
  releaseManifestText,
  releaseConfigText,
  releaseWorkflow,
  claudePluginText,
  rootReadme,
  packageReadme,
  agents,
  installGuide,
  integrationsReadme,
  docsConfig,
  versioning,
] = await Promise.all([
  read(projectPath),
  read(manifestPath),
  read(releaseManifestPath),
  read(releaseConfigPath),
  read(releaseWorkflowPath),
  read(claudePluginPath),
  read("README.md"),
  read("src/Kioku.Mcp.Server/README.md"),
  read("AGENTS.md"),
  read("docs/install.md"),
  read("integrations/README.md"),
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

const claudePlugin = parseJson(claudePluginText, claudePluginPath);
if (claudePlugin.version !== version) {
  failures.push(`${claudePluginPath}: version ${claudePlugin.version ?? "missing"} does not match ${version}`);
}

const releaseManifest = parseJson(releaseManifestText, releaseManifestPath);
if (releaseManifest["."] !== version) {
  failures.push(`${releaseManifestPath}: package version ${releaseManifest["."] ?? "missing"} does not match ${version}`);
}

for (const [relativePath, source] of [
  ["README.md", rootReadme],
  ["src/Kioku.Mcp.Server/README.md", packageReadme],
  ["AGENTS.md", agents],
  ["docs/install.md", installGuide],
  ["integrations/README.md", integrationsReadme],
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
    "docs/install.md",
    "integrations/README.md",
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

  if (!extraFiles.some((entry) =>
    entry.type === "json" &&
    entry.path === claudePluginPath &&
    entry.jsonpath === "$.version")) {
    failures.push(`${releaseConfigPath}: missing $.version update for ${claudePluginPath}`);
  }
}

validateReleaseSupplyChain(releaseWorkflow);

if (failures.length > 0) {
  console.error("[error] Release-facing documentation or supply-chain gates are inconsistent:");
  for (const failure of failures) console.error(`- ${failure}`);
  process.exit(1);
}

console.log(`[ok] Release-facing documentation matches server package version ${version}.`);
console.log(`[ok] Release supply-chain workflow pins Syft ${pinnedSyftVersion}, verifies its digest, retries downloads, and signs explicit per-RID SBOMs.`);

function validateReleaseSupplyChain(source) {
  const requiredFragments = [
    [`SYFT_VERSION: "${pinnedSyftVersion}"`, `must pin Syft ${pinnedSyftVersion}`],
    [`SYFT_SHA256: "${pinnedSyftSha256}"`, "must pin the reviewed Syft linux-amd64 SHA-256"],
    ["--retry 5", "must retry transient Syft downloads"],
    ["--retry-all-errors", "must retry transport-level Syft download failures"],
    ["sha256sum --check -", "must verify the pinned Syft archive before extraction"],
    ["syft scan \"dir:publish/${{ matrix.rid }}\"", "must generate each RID SBOM explicitly"],
    ["--output \"spdx-json=${{ matrix.artifact-name }}.spdx.json\"", "must name each RID SBOM explicitly"],
    ["test -s \"${{ matrix.artifact-name }}.spdx.json\"", "must fail when an RID SBOM is missing or empty"],
    ["sigstore/cosign-installer@v4.1.2", "must use the reviewed Cosign installer with download retries"],
    ["fail-fast: false", "must collect build evidence from every release RID even when one matrix leg fails"],
  ];

  for (const [fragment, description] of requiredFragments) {
    if (!source.includes(fragment)) {
      failures.push(`${releaseWorkflowPath}: ${description}`);
    }
  }

  if (source.includes("anchore/sbom-action@")) {
    failures.push(`${releaseWorkflowPath}: must not reintroduce implicit per-matrix Syft downloads through anchore/sbom-action`);
  }

  if (source.includes("continue-on-error: true")) {
    failures.push(`${releaseWorkflowPath}: release security or publication gates must remain fail-closed`);
  }
}

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
