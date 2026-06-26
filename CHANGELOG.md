# Changelog

## [1.8.0-beta.1](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta...v1.8.0-beta.1) (2026-06-26)


### Features

* **server:** add delete_note and revert_note tools ([#48](https://github.com/sandovaldavid/kioku/issues/48)) ([c6b9194](https://github.com/sandovaldavid/kioku/commit/c6b919467d0a05d964b5b7610ceb92746408f93b))
* **server:** add FolderRanker service, zettel filename format, auto-folder ([#52](https://github.com/sandovaldavid/kioku/issues/52)) ([cb9bcef](https://github.com/sandovaldavid/kioku/commit/cb9bcef6fe3aeb88b78aadf2f2b15f29dcee0ffd))
* **server:** add staging tools — stage_note, stage_all, unstage_note, commit_staged ([#50](https://github.com/sandovaldavid/kioku/issues/50)) ([0df3660](https://github.com/sandovaldavid/kioku/commit/0df36607c5f264000ffacea37d43a26f402f1d4c))


### Bug Fixes

* **server:** resolve note lookup bugs from diagnostics ([#51](https://github.com/sandovaldavid/kioku/issues/51)) ([7dc3c17](https://github.com/sandovaldavid/kioku/commit/7dc3c17a7ae2c4651b7883e8821b8854dbe199a0))

## [1.8.0-beta](https://github.com/sandovaldavid/kioku/compare/v1.7.1-beta...v1.8.0-beta) (2026-06-26)


### Features

* **plugin:** package Obsidian plugin files as ZIP for easy installation ([#43](https://github.com/sandovaldavid/kioku/issues/43)) ([8598368](https://github.com/sandovaldavid/kioku/commit/8598368c12f15e16cc32b51468ec236ab41a6466))


### Bug Fixes

* **plugin:** preserve requestId in dispatch response for all bridge commands ([#46](https://github.com/sandovaldavid/kioku/issues/46)) ([2a28318](https://github.com/sandovaldavid/kioku/commit/2a28318dc003ce1d554f82991a61ef112800ff8f))
* **server:** resolve 5 bugs in git, prepend, session, rename, and extract tools ([#47](https://github.com/sandovaldavid/kioku/issues/47)) ([7bf0a50](https://github.com/sandovaldavid/kioku/commit/7bf0a50db46a37231a115970e7131a829233a461))
* **server:** resolve false positives in tags, broken links, and backlinks ([aa1fe25](https://github.com/sandovaldavid/kioku/commit/aa1fe254ffd4572e95c8b74944dd709026891816))

## [1.7.1-beta](https://github.com/sandovaldavid/kioku/compare/v1.7.0...v1.7.1-beta) (2026-06-26)


### Bug Fixes

* **server:** add prerelease versioning strategy for develop branch ([#41](https://github.com/sandovaldavid/kioku/issues/41)) ([06c842f](https://github.com/sandovaldavid/kioku/commit/06c842ff54b77f97172ebd604a06d591f48a832b))

## [1.7.0](https://github.com/sandovaldavid/kioku/compare/v1.6.3...v1.7.0) (2026-06-26)


### Features

* **server:** change create_folder_readme to use folder name for Folder Notes plugin compatibility ([#39](https://github.com/sandovaldavid/kioku/issues/39)) ([aaa8668](https://github.com/sandovaldavid/kioku/commit/aaa8668545bfea5a92488142ce3472363de3db7a))

## [1.6.3](https://github.com/sandovaldavid/kioku/compare/v1.6.2...v1.6.3) (2026-06-26)


### Bug Fixes

* **plugin:** bundle ws correctly and refactor command dispatch to registry pattern ([#36](https://github.com/sandovaldavid/kioku/issues/36)) ([ef2bd4b](https://github.com/sandovaldavid/kioku/commit/ef2bd4b60aca154d83cd8f6bf5fe7c8f7318d3b5))

## [1.6.2](https://github.com/sandovaldavid/kioku/compare/v1.6.1...v1.6.2) (2026-06-25)


### Bug Fixes

* **plugin:** replace as any casts with typed KiokuApp interface, fix deprecated activeLeaf ([#31](https://github.com/sandovaldavid/kioku/issues/31)) ([ea14f85](https://github.com/sandovaldavid/kioku/commit/ea14f854a902f4d3653f896ad1c1a4ebfb6c6fdb))
* **server:** add missing TrimmerRoots.xml, fill .mcp/server.json placeholders, remove unused import ([#28](https://github.com/sandovaldavid/kioku/issues/28)) ([6454ab6](https://github.com/sandovaldavid/kioku/commit/6454ab643d48ddcfba551a2dc66e416514660710))
* **server:** improve HttpClient config and WebSocket exception handling ([#30](https://github.com/sandovaldavid/kioku/issues/30)) ([debc5c3](https://github.com/sandovaldavid/kioku/commit/debc5c3e5ee6ee0de18c8e4d12323ec4cf6751c8))

## [1.6.1](https://github.com/sandovaldavid/kioku/compare/v1.6.0...v1.6.1) (2026-06-25)


### Bug Fixes

* **release:** add checkout step to attach-artifacts job ([#26](https://github.com/sandovaldavid/kioku/issues/26)) ([b4ca7b4](https://github.com/sandovaldavid/kioku/commit/b4ca7b4642f1f3717bf6cf68e52fcfb8681e4f17))

## [1.6.0](https://github.com/sandovaldavid/kioku/compare/v1.5.0...v1.6.0) (2026-06-25)


### Features

* **server:** add css snippet removal tool (Rama N) ([#24](https://github.com/sandovaldavid/kioku/issues/24)) ([52a0493](https://github.com/sandovaldavid/kioku/commit/52a0493181214b810a0f4cf1c299650f5702d706))
* **server:** add research validation tool (Rama M) ([#23](https://github.com/sandovaldavid/kioku/issues/23)) ([57c16bc](https://github.com/sandovaldavid/kioku/commit/57c16bcfd94694df73eb9037c8a1379596e32990))

## [1.5.0](https://github.com/sandovaldavid/kioku/compare/v1.4.0...v1.5.0) (2026-06-25)


### Features

* **server:** add git integration tools (Rama J) ([#19](https://github.com/sandovaldavid/kioku/issues/19)) ([4b536cc](https://github.com/sandovaldavid/kioku/commit/4b536cc57fc3e2807f5a775a51a0b14b6679e144))
* **server:** add graph analysis tools (Rama I) ([#18](https://github.com/sandovaldavid/kioku/issues/18)) ([e17f45f](https://github.com/sandovaldavid/kioku/commit/e17f45fa7c8b4cf2c963cec9bbdf5649803ecb2d))
* **server:** extend asset tools with normalization (Rama K) ([#20](https://github.com/sandovaldavid/kioku/issues/20)) ([f70fba0](https://github.com/sandovaldavid/kioku/commit/f70fba0bc60fe29fd629a458287155cfe0be7cc1))
* **server:** extend session context tools (Rama L) ([#21](https://github.com/sandovaldavid/kioku/issues/21)) ([58d3ae8](https://github.com/sandovaldavid/kioku/commit/58d3ae87b66a3474acc6ba5f4de8a95ce7cb135c))

## [1.4.0](https://github.com/sandovaldavid/kioku/compare/v1.3.0...v1.4.0) (2026-06-25)


### Features

* **plugin,server:** add 5 editor command tools across server and plugin ([f586290](https://github.com/sandovaldavid/kioku/commit/f5862903d739d8770bdd5feb0f4c7f29b5d51b6c))
* **server:** add asset tools for vault file management ([2530079](https://github.com/sandovaldavid/kioku/commit/25300793f4e92ca03d82491fb11254254ebd3920))
* **server:** add vault routing tools for intelligent note organization ([dade9c7](https://github.com/sandovaldavid/kioku/commit/dade9c786be302351aeaca23d4c9012bab48e1d4))

## [1.3.0](https://github.com/sandovaldavid/kioku/compare/v1.2.0...v1.3.0) (2026-06-25)


### Features

* **server:** register PluginIntegrationTools and remove RandomNumberTools scaffolding ([63e053e](https://github.com/sandovaldavid/kioku/commit/63e053e5a57335e8a661f979db25a20f3a82db53))

## [1.2.0](https://github.com/sandovaldavid/kioku/compare/v1.1.0...v1.2.0) (2026-06-25)


### Features

* **plugin:** add UI plugin commands and CSS theming MCP tools ([6e1dbfd](https://github.com/sandovaldavid/kioku/commit/6e1dbfd422c4acb5d8539917ab9b4766127b9b89))
* **server:** add knowledge graph tools ([#13](https://github.com/sandovaldavid/kioku/issues/13)) ([0e881f6](https://github.com/sandovaldavid/kioku/commit/0e881f6161b84804e86aac9715a0d6c982a27fbd))
* **server:** add research, export, and gist sharing tools ([#14](https://github.com/sandovaldavid/kioku/issues/14)) ([9e56c5a](https://github.com/sandovaldavid/kioku/commit/9e56c5a1ab5f5cb565ccc7c511ce4f82b338d10e))
* **server:** add session context and work activity tracking tools ([af47f41](https://github.com/sandovaldavid/kioku/commit/af47f4125767ec41c5471ab3ef73fc92cc7b1b5d))
* **server:** add task management MCP tools ([4445439](https://github.com/sandovaldavid/kioku/commit/444543951084b458b1e5d0bc2c063312aa5786e1))
* **server:** add vault organization and taxonomy management tools ([96fd7eb](https://github.com/sandovaldavid/kioku/commit/96fd7eb4cb8478ec9843d11b84ab9fc19395dae3))
* **server:** add workflow chain tools for templates and action items ([268d27b](https://github.com/sandovaldavid/kioku/commit/268d27bc41911b08e7c34e66b6c9e9dfbf8617f8))
* **server:** add Zettelkasten knowledge management tools ([9e365a4](https://github.com/sandovaldavid/kioku/commit/9e365a4441bb1a90269e2507307716862a898777))
* **server:** HTTP-SSE transport with dual-mode startup and API key auth ([84ac108](https://github.com/sandovaldavid/kioku/commit/84ac108bf07625602d47b4fff45880386c640568))

## [1.1.0](https://github.com/sandovaldavid/kioku/compare/v1.0.0...v1.1.0) (2026-06-25)


### Features

* initial setup — server tooling, logging, semantic search ([#2](https://github.com/sandovaldavid/kioku/issues/2)) ([347e5b0](https://github.com/sandovaldavid/kioku/commit/347e5b0371031e0bfcce51ba854bca99097f3190))
* **plugin:** add Obsidian WebSocket bridge plugin ([c7f25cc](https://github.com/sandovaldavid/kioku/commit/c7f25ccf1c24959a854b63466b7bdafba0f710bb))
* **server:** add MCP server with vault indexing and note tools ([83475c2](https://github.com/sandovaldavid/kioku/commit/83475c2a2986a2c711b03f7d3b11e2e70de4b6b8))
