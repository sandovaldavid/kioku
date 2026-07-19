import { existsSync, readFileSync, statSync } from "node:fs";

const manifest = JSON.parse(readFileSync("manifest.json", "utf8"));
const pkg = JSON.parse(readFileSync("package.json", "utf8"));
const versions = JSON.parse(readFileSync("versions.json", "utf8"));
const failures = [];

const requiredManifestFields = ["id", "name", "version", "minAppVersion", "description", "author"];
for (const field of requiredManifestFields) {
  if (typeof manifest[field] !== "string" || manifest[field].trim().length === 0) {
    failures.push(`manifest.json is missing a valid '${field}'.`);
  }
}

if (manifest.id !== "kioku-mcp") failures.push("manifest id must remain 'kioku-mcp'.");
if (manifest.isDesktopOnly !== true) failures.push("the bridge must remain desktop-only.");
if (manifest.version !== pkg.version)
  failures.push("package.json and manifest.json versions differ.");
if (versions[manifest.version] !== manifest.minAppVersion) {
  failures.push("versions.json must map the current plugin version to minAppVersion.");
}

for (const asset of ["main.js", "manifest.json", "styles.css"]) {
  if (!existsSync(asset)) failures.push(`missing release asset: ${asset}`);
}

if (existsSync("main.js")) {
  const bundle = readFileSync("main.js", "utf8");
  const size = statSync("main.js").size;
  if (size > 512 * 1024) failures.push(`main.js exceeds the 512 KiB budget (${size} bytes).`);
  if (bundle.includes("sourceMappingURL="))
    failures.push("production main.js contains a source map reference.");
  if (bundle.includes("console.debug("))
    failures.push("production main.js contains debug logging.");
}

if (failures.length > 0) {
  console.error(failures.map((failure) => `- ${failure}`).join("\n"));
  process.exit(1);
}

console.log(
  `Validated ${manifest.id} v${manifest.version} for Obsidian ${manifest.minAppVersion}+ (${(statSync("main.js").size / 1024).toFixed(1)} KiB).`
);
