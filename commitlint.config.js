/** @type {import('@commitlint/types').UserConfig} */
const config = {
  extends: ["@commitlint/config-conventional"],
  rules: {
    "scope-empty": [2, "never"],
    "scope-enum": [
      2,
      "always",
      [
        "server",
        "plugin",
        "docs",
        "ci",
        "config",
        "deps",
        "release",
        "integrations",
      ],
    ],
    "scope-case": [2, "always", "lower-case"],
    "header-max-length": [2, "always", 100],
    "subject-full-stop": [2, "never", "."],
    "subject-case": [
      2,
      "never",
      ["sentence-case", "start-case", "pascal-case", "upper-case"],
    ],
  },
};

module.exports = config;
