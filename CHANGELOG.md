# Changelog

## [2.0.2](https://github.com/sandovaldavid/kioku/compare/v2.0.1...v2.0.2) (2026-07-04)


### Bug Fixes

* **server:** pack real LICENSE file and enable deterministic builds ([#184](https://github.com/sandovaldavid/kioku/issues/184)) ([f047a65](https://github.com/sandovaldavid/kioku/commit/f047a65e450bf22fa48414e50c718c5d2b0612b8))

## [2.0.1](https://github.com/sandovaldavid/kioku/compare/v2.0.0...v2.0.1) (2026-07-04)


### Bug Fixes

* **ci:** fix publish-nuget job and rename installed tool command to kioku ([c595e94](https://github.com/sandovaldavid/kioku/commit/c595e941893ebe6d8c57e0fcc1e791c1f015ec17))
* **ci:** fix publish-nuget job and rename installed tool command to kioku ([ca8eff7](https://github.com/sandovaldavid/kioku/commit/ca8eff78a937ddd5420227e6634afc8f6dfa1c07))

## [2.0.0](https://github.com/sandovaldavid/kioku/compare/v1.0.0...v2.0.0) (2026-07-04)


### ⚠ BREAKING CHANGES

* **release:** the MCP tool suggest_tags in NoteQueryTools (core, always registered) is renamed to inspect_note_tags. Agents calling the read-only diagnostic variant by name must update to inspect_note_tags.

### Features

* **plugin:** add Obsidian WebSocket bridge plugin ([c7f25cc](https://github.com/sandovaldavid/kioku/commit/c7f25ccf1c24959a854b63466b7bdafba0f710bb))
* **server:** add MCP server with vault indexing and note tools ([83475c2](https://github.com/sandovaldavid/kioku/commit/83475c2a2986a2c711b03f7d3b11e2e70de4b6b8))


### Bug Fixes

* **ci:** pass explicit target-branch to release-please action ([#156](https://github.com/sandovaldavid/kioku/issues/156)) ([8b7aa8d](https://github.com/sandovaldavid/kioku/commit/8b7aa8d2d16601b9388ffd5f58476e84f979988d))


### Miscellaneous Chores

* **release:** sync develop into main — P0 through P3 backlog complete ([#153](https://github.com/sandovaldavid/kioku/issues/153)) ([5a09001](https://github.com/sandovaldavid/kioku/commit/5a09001103b77dd3b3b9f05e594ba58fde09bcc5))

## [2.0.0-beta.8](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.8...v2.0.0-beta.8) (2026-07-04)


### ⚠ BREAKING CHANGES

* **server:** the MCP tool suggest_tags in NoteQueryTools (core, always registered) is renamed to inspect_note_tags. Agents calling the read-only diagnostic variant by name must update to inspect_note_tags.

### Features

* **plugin:** add bridge status bar item and control commands ([#126](https://github.com/sandovaldavid/kioku/issues/126)) ([c03127f](https://github.com/sandovaldavid/kioku/commit/c03127f26aef557c1588dd9be1b8b5f8c62a9d25))
* **server:** add bibtex import and export for literature notes ([#145](https://github.com/sandovaldavid/kioku/issues/145)) ([8a98632](https://github.com/sandovaldavid/kioku/commit/8a986326a61e64d0cb0774eefa9b2954348f2bfa))
* **server:** add citation graph analysis for literature notes ([#147](https://github.com/sandovaldavid/kioku/issues/147)) ([8abbbd6](https://github.com/sandovaldavid/kioku/commit/8abbbd677fd737f9042d1eedef6db6db4a6dc9aa))
* **server:** add generate_digest tool for daily and weekly reviews ([#137](https://github.com/sandovaldavid/kioku/issues/137)) ([623c50e](https://github.com/sandovaldavid/kioku/commit/623c50ea09b7de8d8c004ed15fa331fe4c728d55))
* **server:** add generate_flashcards tool with spaced-repetition and anki output ([#149](https://github.com/sandovaldavid/kioku/issues/149)) ([7c602bb](https://github.com/sandovaldavid/kioku/commit/7c602bb54121b9c4ae2e0d16d4322b363a7bda5a))
* **server:** add local text generation service with KIOKU_GEN_MODEL ([#135](https://github.com/sandovaldavid/kioku/issues/135)) ([94c6d37](https://github.com/sandovaldavid/kioku/commit/94c6d37ace7ca050de6033c09d2d302ee2aa0813))
* **server:** add optional shared-token auth to the obsidian bridge ([#132](https://github.com/sandovaldavid/kioku/issues/132)) ([86b3a39](https://github.com/sandovaldavid/kioku/commit/86b3a3945f51cd0ed691fccc878f95edc79a3788))
* **server:** add process_inbox batch triage tool ([#139](https://github.com/sandovaldavid/kioku/issues/139)) ([0ad00b6](https://github.com/sandovaldavid/kioku/commit/0ad00b68105df08a743ec419873d153e2746ec1d))
* **server:** add suggest_links and apply_link_suggestions tools ([#141](https://github.com/sandovaldavid/kioku/issues/141)) ([377ab66](https://github.com/sandovaldavid/kioku/commit/377ab6635ef3042038ab291568efff7e594d4bd5))
* **server:** expose latent bridge commands as MCP tools ([#124](https://github.com/sandovaldavid/kioku/issues/124)) ([f38cc80](https://github.com/sandovaldavid/kioku/commit/f38cc80cf527822f9e28e02109b09fbc76a03eaa))
* **server:** expose mcp prompts and note resources ([#143](https://github.com/sandovaldavid/kioku/issues/143)) ([e3eb0f2](https://github.com/sandovaldavid/kioku/commit/e3eb0f2625afeba0ae829f953128efc3ac3a0468))
* **server:** incremental re-embedding with content hashes and progress ([#151](https://github.com/sandovaldavid/kioku/issues/151)) ([3ac0289](https://github.com/sandovaldavid/kioku/commit/3ac02892ac8a20a664ef959d41b38dc31ab79a4c))
* **server:** update inbound wikilinks on move_note and rename_note ([#130](https://github.com/sandovaldavid/kioku/issues/130)) ([46480fd](https://github.com/sandovaldavid/kioku/commit/46480fd900f4a60f4e8c8e20d6becf14fa8a3a40))


### Bug Fixes

* **ci:** extract main.js from ZIP artifact for BRAT upload ([50dbf55](https://github.com/sandovaldavid/kioku/commit/50dbf552179005a90037d175b49d4531eae38040))
* **server:** make ObsidianBridgeService's connection teardown reentrant-safe ([#134](https://github.com/sandovaldavid/kioku/issues/134)) ([f949c26](https://github.com/sandovaldavid/kioku/commit/f949c2668b3a5864d196c37e5b004b2cf89f8e07))
* **server:** move merge-conflict tools out of the plugin capability group ([#121](https://github.com/sandovaldavid/kioku/issues/121)) ([7fcbec9](https://github.com/sandovaldavid/kioku/commit/7fcbec9fed87588cbbde67d92edb3ecf20e95e23))
* **server:** rename duplicate suggest_tags query tool to inspect_note_tags ([#120](https://github.com/sandovaldavid/kioku/issues/120)) ([e62d7fd](https://github.com/sandovaldavid/kioku/commit/e62d7fd92252b8a8e7d5f897e1a06ba0de655f34))

## [1.8.0-beta.8](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.7...v1.8.0-beta.8) (2026-06-29)


### Features

* **ci:** add github actions workflow for opencode integration ([#116](https://github.com/sandovaldavid/kioku/issues/116)) ([60eac58](https://github.com/sandovaldavid/kioku/commit/60eac5866e2c26af6ad589c4a4824db96e1b5775))

## [1.8.0-beta.7](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.6...v1.8.0-beta.7) (2026-06-28)


### Features

* **ci:** generate SBOMs and sign release artifacts with cosign ([#110](https://github.com/sandovaldavid/kioku/issues/110)) ([107d4d9](https://github.com/sandovaldavid/kioku/commit/107d4d9e8bb55b9a602afc40a5ba8eab2b56bc84))
* **server:** add one-line install script and package manager placeholders ([#108](https://github.com/sandovaldavid/kioku/issues/108)) ([0f4a9fa](https://github.com/sandovaldavid/kioku/commit/0f4a9fa5191d34e3ab929aa9b9a07baeb7ace6a9))
* **server:** add opt-in Sentry crash reporting ([#112](https://github.com/sandovaldavid/kioku/issues/112)) ([fc1e1d8](https://github.com/sandovaldavid/kioku/commit/fc1e1d8b83e424d10111e5db775bfd1c716d8691))
* **server:** add opt-in tool-call telemetry ([#103](https://github.com/sandovaldavid/kioku/issues/103)) ([93b709d](https://github.com/sandovaldavid/kioku/commit/93b709d4c630bbd1588bdb54000ba68c60d5b2df))
* **server:** add optional JSON format for query tools ([#114](https://github.com/sandovaldavid/kioku/issues/114)) ([4022aa5](https://github.com/sandovaldavid/kioku/commit/4022aa54f746dc4061aa57a1dcd46057dcff9865))
* **server:** add tool capability groups / namespacing ([#106](https://github.com/sandovaldavid/kioku/issues/106)) ([e0719e4](https://github.com/sandovaldavid/kioku/commit/e0719e419f0d6a74d32ab794cc45b21e6f698a40))

## [1.8.0-beta.6](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.5...v1.8.0-beta.6) (2026-06-28)


### Features

* **ci:** add code coverage reporting with coverlet and Codecov ([#75](https://github.com/sandovaldavid/kioku/issues/75)) ([2c385f7](https://github.com/sandovaldavid/kioku/commit/2c385f71a62c48b545c5008c600c4346b34d4916))
* **ci:** add dependency vulnerability scanning ([7b08098](https://github.com/sandovaldavid/kioku/commit/7b0809867513744a2f7925ecb82f2ff290b3bdc9))
* **ci:** add Docker deployment with Ollama integration ([#76](https://github.com/sandovaldavid/kioku/issues/76)) ([259387a](https://github.com/sandovaldavid/kioku/commit/259387acf58108aa79de8c87ecded3eca3c556d0))
* **ci:** add NuGet package publish on release ([#77](https://github.com/sandovaldavid/kioku/issues/77)) ([e5c9159](https://github.com/sandovaldavid/kioku/commit/e5c91592e6e1df6d3572c8abaa5cccd292d6d642))
* **docs:** add auto-generated MCP tools reference ([#81](https://github.com/sandovaldavid/kioku/issues/81)) ([9de9c1e](https://github.com/sandovaldavid/kioku/commit/9de9c1e311751cd031300c333283cb8c8d6e247c))
* **plugin:** prepare for Obsidian Community Store submission ([#78](https://github.com/sandovaldavid/kioku/issues/78)) ([1ded917](https://github.com/sandovaldavid/kioku/commit/1ded917c1ab7c42d048cfb31931d1618628e3624))
* **server:** add BenchmarkDotNet project for performance tracking ([#101](https://github.com/sandovaldavid/kioku/issues/101)) ([2616e70](https://github.com/sandovaldavid/kioku/commit/2616e7041cba4aeb511473e8a934a4b26cfe2de4))
* **server:** add EmbeddingModelRegistry for known Ollama models ([#93](https://github.com/sandovaldavid/kioku/issues/93)) ([0a8263c](https://github.com/sandovaldavid/kioku/commit/0a8263c607a7646f34da923eb27c6ff6a5610eaf))
* **server:** add pagination to list_notes ([#97](https://github.com/sandovaldavid/kioku/issues/97)) ([e3e7883](https://github.com/sandovaldavid/kioku/commit/e3e7883c642e431a8a2f0d4562c173662c5a7a9c))
* **server:** add soft-delete with trash for delete_note ([#79](https://github.com/sandovaldavid/kioku/issues/79)) ([661c396](https://github.com/sandovaldavid/kioku/commit/661c39677caf761dcc831d1807874bd32100d4c3))
* **server:** config-v2 polish — built-in template variables and malformed config warning ([#95](https://github.com/sandovaldavid/kioku/issues/95)) ([993050f](https://github.com/sandovaldavid/kioku/commit/993050f8acbe5da5e5a989c327e3ed292fdf78c1))
* **server:** expose operational counters in ping and get_index_status ([#89](https://github.com/sandovaldavid/kioku/issues/89)) ([c85e632](https://github.com/sandovaldavid/kioku/commit/c85e632062ed37f93fef57c4dd9f5dc9c9a1205b))
* **server:** flush embedding cache on graceful shutdown ([#99](https://github.com/sandovaldavid/kioku/issues/99)) ([a06d47a](https://github.com/sandovaldavid/kioku/commit/a06d47aaf01121505b6cc68eb637531294b61610))
* **server:** introduce standardized KiokuError taxonomy ([#91](https://github.com/sandovaldavid/kioku/issues/91)) ([0b29768](https://github.com/sandovaldavid/kioku/commit/0b29768580a2854156f1a4815ddaba893f2f8971))


### Bug Fixes

* **plugin:** add payload validation for all bridge handlers ([#69](https://github.com/sandovaldavid/kioku/issues/69)) ([6c19e63](https://github.com/sandovaldavid/kioku/commit/6c19e63fecc25b20ff69568e9b24eab785644683))
* **plugin:** await openLinkText in cmdOpenFile ([#67](https://github.com/sandovaldavid/kioku/issues/67)) ([b81da7a](https://github.com/sandovaldavid/kioku/commit/b81da7aee77fdd0a2fd7e8844d74ec4715968246))
* **plugin:** use Plugin.manifest for get-app-version ([#74](https://github.com/sandovaldavid/kioku/issues/74)) ([30bf43a](https://github.com/sandovaldavid/kioku/commit/30bf43a6d37ce66f6c1104fa6d9317026d5354d8))
* **server,plugin:** add protocol version handshake to bridge ([#71](https://github.com/sandovaldavid/kioku/issues/71)) ([d9f7629](https://github.com/sandovaldavid/kioku/commit/d9f7629411eb34b2aa4b99e072959c93dbf4e231))
* **server:** add BootstrapLogger for early startup errors ([#73](https://github.com/sandovaldavid/kioku/issues/73)) ([a4af0a5](https://github.com/sandovaldavid/kioku/commit/a4af0a55bebdbea6db7af690e0b9ebb051523146))
* **server:** add model/dimension stamping to embedding cache ([#70](https://github.com/sandovaldavid/kioku/issues/70)) ([e05674a](https://github.com/sandovaldavid/kioku/commit/e05674ab46467df0617c64ff3f6803dcdaf6f7da))
* **server:** handle reindex exceptions in debounced task ([#68](https://github.com/sandovaldavid/kioku/issues/68)) ([f243da7](https://github.com/sandovaldavid/kioku/commit/f243da748c1314af78f2db9db45172107c92e76d))
* **server:** use IHttpClientFactory instead of static/new HttpClient ([#72](https://github.com/sandovaldavid/kioku/issues/72)) ([7a8f431](https://github.com/sandovaldavid/kioku/commit/7a8f4310b42ce7316e98127dd9efdc0893654b4a))

## [1.8.0-beta.5](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.4...v1.8.0-beta.5) (2026-06-27)


### Features

* **plugin:** add BRAT support and fundingUrl to manifest ([#64](https://github.com/sandovaldavid/kioku/issues/64)) ([ac5ebd9](https://github.com/sandovaldavid/kioku/commit/ac5ebd998dbb2330da10bdb5cf0cf6f90afa16ae))


### Bug Fixes

* **plugin:** restore bridge startup error Notice ([#60](https://github.com/sandovaldavid/kioku/issues/60)) ([81761b6](https://github.com/sandovaldavid/kioku/commit/81761b65216ac19b7fb35907c6830048cda745da))
* **server:** add vault path-traversal containment for all write tools ([#59](https://github.com/sandovaldavid/kioku/issues/59)) ([54adcf2](https://github.com/sandovaldavid/kioku/commit/54adcf21ecd2d6bd27a9baf56eafc00810d3f0a1))

## [1.8.0-beta.4](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.3...v1.8.0-beta.4) (2026-06-27)


### Bug Fixes

* **server:** correct table cell extraction and use cross-platform invalid filename chars ([#57](https://github.com/sandovaldavid/kioku/issues/57)) ([3e743df](https://github.com/sandovaldavid/kioku/commit/3e743dfdb09419eb702114b6a7363d9731f5c440))

## [1.8.0-beta.3](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.2...v1.8.0-beta.3) (2026-06-27)


### Features

* **server:** config v2 templates, macOS support, plugin refactor, and tests ([#55](https://github.com/sandovaldavid/kioku/issues/55)) ([0c2597b](https://github.com/sandovaldavid/kioku/commit/0c2597b3fefc0118a57c5446a190703ed00e098f))

## [1.8.0-beta.2](https://github.com/sandovaldavid/kioku/compare/v1.8.0-beta.1...v1.8.0-beta.2) (2026-06-26)


### Features

* **server:** add VaultConfigService with .kioku/config.yml support ([#53](https://github.com/sandovaldavid/kioku/issues/53)) ([175c50a](https://github.com/sandovaldavid/kioku/commit/175c50a87d15153d190cc684a62e049efb817e82))

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
