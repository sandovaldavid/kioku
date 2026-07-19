import { readFileSync, writeFileSync } from "fs";

const targetVersion = process.env.npm_package_version;
if (!targetVersion) {
  console.error("No version specified. Run via: npm version <type>");
  process.exit(1);
}

// Read manifest.json and update version
const manifest = JSON.parse(readFileSync("manifest.json", "utf8"));
manifest.version = targetVersion;
writeFileSync("manifest.json", JSON.stringify(manifest, null, 2) + "\n");

// Read versions.json and add entry
const versionsPath = "versions.json";
let versions = {};
try {
  versions = JSON.parse(readFileSync(versionsPath, "utf8"));
} catch {
  // File may not exist yet
}
versions[targetVersion] = manifest.minAppVersion;
writeFileSync(versionsPath, JSON.stringify(versions, null, 2) + "\n");

console.log(
  `Version bumped to ${targetVersion}; minimum Obsidian version ${manifest.minAppVersion}.`
);
