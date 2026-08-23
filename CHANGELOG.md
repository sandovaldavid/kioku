# Changelog

## [3.1.2](https://github.com/sandovaldavid/kioku/compare/v3.1.1...v3.1.2) (2026-08-23)


### Bug Fixes

* **ci:** make conventional commit validation back-sync aware ([#434](https://github.com/sandovaldavid/kioku/issues/434)) ([25a1d63](https://github.com/sandovaldavid/kioku/commit/25a1d63e283151807d8155127f96b7ded86def21))
* **server:** make read_note resilient to concurrent file access ([#451](https://github.com/sandovaldavid/kioku/issues/451)) ([0e89a67](https://github.com/sandovaldavid/kioku/commit/0e89a676dbba9d0a74d109131a2e4c16990d90f8))
* **server:** recover from corrupted embeddings cache on startup ([#447](https://github.com/sandovaldavid/kioku/issues/447)) ([f838809](https://github.com/sandovaldavid/kioku/commit/f8388099e0d107ac15a0268079c69e9015bb16cf))
* **server:** rewrite relative wikilinks after move_note ([#450](https://github.com/sandovaldavid/kioku/issues/450)) ([a54dd31](https://github.com/sandovaldavid/kioku/commit/a54dd316e23b16dfa30af6d8217acc013a59a57a))
* **server:** scope session activity to the session's own project ([#452](https://github.com/sandovaldavid/kioku/issues/452)) ([96e873b](https://github.com/sandovaldavid/kioku/commit/96e873b11280c5bc8b4db4ee16a8b8ae2c73293e))
* **server:** stop find_orphan_assets from trashing in-use assets ([#449](https://github.com/sandovaldavid/kioku/issues/449)) ([87377f1](https://github.com/sandovaldavid/kioku/commit/87377f1005ddb924d1bb4dd0ef6870e51fd60f37))
* **server:** support parenthesis-delimited BibTeX entries ([#453](https://github.com/sandovaldavid/kioku/issues/453)) ([46101f0](https://github.com/sandovaldavid/kioku/commit/46101f0b47c3a9aad92e3c11734f8a4e1f3c3f4a))


### Performance Improvements

* **server:** avoid full-vault scans on every search and suggest_folder call ([#454](https://github.com/sandovaldavid/kioku/issues/454)) ([2964717](https://github.com/sandovaldavid/kioku/commit/296471792017771dc5a35f20c55f79ce4f3cb4ea)), closes [#444](https://github.com/sandovaldavid/kioku/issues/444)

## [3.1.1](https://github.com/sandovaldavid/kioku/compare/v3.1.0...v3.1.1) (2026-08-12)


### Bug Fixes

* **integrations:** keep Claude and Antigravity bundles aligned ([#422](https://github.com/sandovaldavid/kioku/issues/422)) ([dc5947b](https://github.com/sandovaldavid/kioku/commit/dc5947be71087d84a249446ec4f8401c216cff14))
* **integrations:** use vault env for Claude ([#423](https://github.com/sandovaldavid/kioku/issues/423)) ([b9fff72](https://github.com/sandovaldavid/kioku/commit/b9fff724ce5a4ed42bbad81c9046c669d8f99911))
* **server:** eliminate pathological reconciliation work ([#428](https://github.com/sandovaldavid/kioku/issues/428)) ([50c70b5](https://github.com/sandovaldavid/kioku/commit/50c70b5d88403e3e8fd186acbd881df7433266a4))

## [3.1.0](https://github.com/sandovaldavid/kioku/compare/v3.0.1...v3.1.0) (2026-08-09)


### Features

* **engineering:** add first-class specs and durable workflow boundary ([#417](https://github.com/sandovaldavid/kioku/issues/417)) ([a22e6fc](https://github.com/sandovaldavid/kioku/commit/a22e6fc35fd6dcdd42d4a2f4a8e3e1ccedd982e6))


### Bug Fixes

* **audit:** expose complete wikilink findings ([#404](https://github.com/sandovaldavid/kioku/issues/404)) ([a3c9ae1](https://github.com/sandovaldavid/kioku/commit/a3c9ae1090a355ef6f3dc44efb2840a1267f37c5))
* **ci:** replace legacy MCP ping liveness check ([#410](https://github.com/sandovaldavid/kioku/issues/410)) ([b863dcb](https://github.com/sandovaldavid/kioku/commit/b863dcb37a327f75330a0c6ab12ab77e8862122f))
* **integrations:** remove personal vault paths from client configurations ([40dd8d0](https://github.com/sandovaldavid/kioku/commit/40dd8d0409ed9132f69bf27e824ec30275f2a7aa))
* **mcp:** harden tool output schemas and safety annotations for ChatGPT ([#387](https://github.com/sandovaldavid/kioku/issues/387)) ([#390](https://github.com/sandovaldavid/kioku/issues/390)) ([6b06186](https://github.com/sandovaldavid/kioku/commit/6b06186d006f362c5fdd80f14432b8aa184a5de7))
* **mcp:** keep cold indexing off the host startup path ([#399](https://github.com/sandovaldavid/kioku/issues/399)) ([a7de4d6](https://github.com/sandovaldavid/kioku/commit/a7de4d6fa54f6a4d74946cab7d23f20d22af614d))
* **server:** classify empty template placeholders ([#406](https://github.com/sandovaldavid/kioku/issues/406)) ([8867ea8](https://github.com/sandovaldavid/kioku/commit/8867ea801ae23a662906ef75868de437f987daaa))
* **server:** make Streamable HTTP stateless explicit ([#411](https://github.com/sandovaldavid/kioku/issues/411)) ([6b0b942](https://github.com/sandovaldavid/kioku/commit/6b0b942a7cba298d293e60df9be9ea98748294d0))
* **server:** make wikilink rewriting resolver-aware ([#397](https://github.com/sandovaldavid/kioku/issues/397)) ([bf5a095](https://github.com/sandovaldavid/kioku/commit/bf5a09576dee8901aff54d2cc610b7a68d20b1db))
* **server:** preserve dotted wikilink basenames ([#403](https://github.com/sandovaldavid/kioku/issues/403)) ([2cd149a](https://github.com/sandovaldavid/kioku/commit/2cd149a510b0ce10888b262550546552064d1aa2))
* **server:** unify wikilink resolution across graph surfaces ([#395](https://github.com/sandovaldavid/kioku/issues/395)) ([46e8037](https://github.com/sandovaldavid/kioku/commit/46e8037eac05111b102cc9ac94a8c868a53a8b42))

## [3.0.1](https://github.com/sandovaldavid/kioku/compare/v3.0.0...v3.0.1) (2026-08-04)


### Bug Fixes

* **release:** align v3 documentation for the 3.0.1 package ([#376](https://github.com/sandovaldavid/kioku/issues/376)) ([3768a8b](https://github.com/sandovaldavid/kioku/commit/3768a8b31939ed7f1036d67d38536b3b867e9656))

## [3.0.0](https://github.com/sandovaldavid/kioku/compare/v2.3.0...v3.0.0) (2026-08-04)


### ⚠ BREAKING CHANGES

* **release:** Kioku 3 removes, renames, or consolidates public MCP tools and changes the default discovery profiles.

### Features

* **ai:** add Kioku project agent profile ([#330](https://github.com/sandovaldavid/kioku/issues/330)) ([eca0d22](https://github.com/sandovaldavid/kioku/commit/eca0d222380ce44ad482c09c54f01882bbd15639))
* **server:** add CAS vault mutations ([#316](https://github.com/sandovaldavid/kioku/issues/316)) ([7dd0562](https://github.com/sandovaldavid/kioku/commit/7dd0562d0f70f638d241037a97797337484eb4e5))
* **server:** add coordination MCP surface ([#317](https://github.com/sandovaldavid/kioku/issues/317)) ([23d3bd3](https://github.com/sandovaldavid/kioku/commit/23d3bd3c660f43f57891295d6c4c610318940ff9)), closes [#308](https://github.com/sandovaldavid/kioku/issues/308)
* **server:** add coordination observability ([#320](https://github.com/sandovaldavid/kioku/issues/320)) ([c6d6c93](https://github.com/sandovaldavid/kioku/commit/c6d6c9331b40da2ba06fd998e6f3f7286cc1f2eb))
* **server:** add durable claims and fencing ([#315](https://github.com/sandovaldavid/kioku/issues/315)) ([271ba90](https://github.com/sandovaldavid/kioku/commit/271ba90a43dbf49e82ed9278f6467ee69f8eb21c))
* **server:** add MCP tool annotations and contract tests ([#273](https://github.com/sandovaldavid/kioku/issues/273)) ([1a64272](https://github.com/sandovaldavid/kioku/commit/1a642724a20dce9903e4ba6e0bdde7b95ea9a924))
* **server:** add reproducible multi-agent handoff demo ([#295](https://github.com/sandovaldavid/kioku/issues/295)) ([3ef871c](https://github.com/sandovaldavid/kioku/commit/3ef871c407984f5ecfe0b918d36b1798c1471028))
* **server:** define coordination contracts ([#313](https://github.com/sandovaldavid/kioku/issues/313)) ([64ce6ab](https://github.com/sandovaldavid/kioku/commit/64ce6ab1d2d1c4c3a17d86adbc642371df83988d))
* **server:** extend Kioku.Benchmarks into a real performance/quality suite ([#296](https://github.com/sandovaldavid/kioku/issues/296)) ([244fd76](https://github.com/sandovaldavid/kioku/commit/244fd769555f52b278b9f2ceec3343bb94a001bf))
* **server:** integrate work sessions with coordination ([#318](https://github.com/sandovaldavid/kioku/issues/318)) ([0314631](https://github.com/sandovaldavid/kioku/commit/0314631ca9970a636e7e191e3a33ab1828a762ad)), closes [#309](https://github.com/sandovaldavid/kioku/issues/309)
* **server:** persist event history ([#314](https://github.com/sandovaldavid/kioku/issues/314)) ([794f9e1](https://github.com/sandovaldavid/kioku/commit/794f9e1b6898fcd1bab83231fcfddfae4c829b98))
* **server:** return typed MCP result envelopes ([#272](https://github.com/sandovaldavid/kioku/issues/272)) ([2faa07d](https://github.com/sandovaldavid/kioku/commit/2faa07de6b56cb83d92db0267cbe528a39efaa13))


### Bug Fixes

* **ci:** match RID-specific packages in package-smoke's local feed mapping ([#346](https://github.com/sandovaldavid/kioku/issues/346)) ([7d732f5](https://github.com/sandovaldavid/kioku/commit/7d732f571114b12dffecc7c9b37deaf0fdcd6a82))
* **docs:** preserve the sidebar on linked documentation pages ([#370](https://github.com/sandovaldavid/kioku/issues/370)) ([ab4d2fa](https://github.com/sandovaldavid/kioku/commit/ab4d2faab3ae637487bebf9b237b552aa3a18ecf))
* **release:** refresh generated v3 version docs ([6a74446](https://github.com/sandovaldavid/kioku/commit/6a74446acbafac9e2eaf8b9373caf9489ac07f99))
* **release:** repair v3 release gates ([c102810](https://github.com/sandovaldavid/kioku/commit/c1028109be93cbd2d78f0aec907892deb461c1e2))
* **server:** align index and MCP results ([#301](https://github.com/sandovaldavid/kioku/issues/301)) ([e8e6bba](https://github.com/sandovaldavid/kioku/commit/e8e6bba0d5bbe76089628ec4d0804579c7efdeb5))
* **server:** close review gaps ([#354](https://github.com/sandovaldavid/kioku/issues/354)) ([9b6733d](https://github.com/sandovaldavid/kioku/commit/9b6733ddbcd34535e08951d66a06dded64209dc4))
* **server:** harden Streamable HTTP transport ([#271](https://github.com/sandovaldavid/kioku/issues/271)) ([8781937](https://github.com/sandovaldavid/kioku/commit/87819374bf8b026875ca318059643eb5932e9b2b))
* **server:** make indexing queue depth race-free ([#289](https://github.com/sandovaldavid/kioku/issues/289)) ([4d77f02](https://github.com/sandovaldavid/kioku/commit/4d77f02655fbd43f298118010ca26582294e76ff))
* **server:** make trash name allocation atomic under concurrent delete_note ([#241](https://github.com/sandovaldavid/kioku/issues/241)) ([fdbf93c](https://github.com/sandovaldavid/kioku/commit/fdbf93c8cf87d10f58fe37683e1448301cfb277e))
* **server:** make work sessions concurrency-safe ([#267](https://github.com/sandovaldavid/kioku/issues/267)) ([a715e05](https://github.com/sandovaldavid/kioku/commit/a715e058ee9ea34b29b13f947d5f94bd226e6c90))
* **server:** normalize typographic dashes in sanitized filenames ([#242](https://github.com/sandovaldavid/kioku/issues/242)) ([e124438](https://github.com/sandovaldavid/kioku/commit/e1244381d01f49209a9076905c39460450a628ad))
* **server:** preserve typed YAML frontmatter ([#266](https://github.com/sandovaldavid/kioku/issues/266)) ([6d91200](https://github.com/sandovaldavid/kioku/commit/6d91200966ffcebee6940a53bd9aff95893f7303))
* **server:** prevent unhandled exceptions from reindex crashing tools ([#243](https://github.com/sandovaldavid/kioku/issues/243)) ([1c7faaf](https://github.com/sandovaldavid/kioku/commit/1c7faafbac698202617d48bf8ce7d1f86fd6759a))
* **server:** repair generated note mutations ([#359](https://github.com/sandovaldavid/kioku/issues/359)) ([2dc1d0d](https://github.com/sandovaldavid/kioku/commit/2dc1d0da254697fd9e1186851e3ef7cbc050da26))
* **server:** resolve vault integration bugs ([#240](https://github.com/sandovaldavid/kioku/issues/240)) ([d0de25c](https://github.com/sandovaldavid/kioku/commit/d0de25c79797c3ab91121b97a03ef75bcf5b4699))


### Performance Improvements

* **server:** add resilient bounded vault indexing pipeline ([#276](https://github.com/sandovaldavid/kioku/issues/276)) ([cb81590](https://github.com/sandovaldavid/kioku/commit/cb81590aa779fb30b1afc1290a862faa4f892c8d))


### Miscellaneous Chores

* **release:** prepare 3.0.0 migration guide ([#360](https://github.com/sandovaldavid/kioku/issues/360)) ([4509904](https://github.com/sandovaldavid/kioku/commit/45099044eed2e40632e3ca389a3fe6a601f3d0d5))

## [2.3.0](https://github.com/sandovaldavid/kioku/compare/v2.2.1...v2.3.0) (2026-07-15)


### Features

* **server:** engineering tool group for per-project ADRs, bugs, plans and knowledge ([#227](https://github.com/sandovaldavid/kioku/issues/227)) ([67ededa](https://github.com/sandovaldavid/kioku/commit/67ededa86253d161409a6050612492c5a9f8ce73))
* **server:** heading-aware chunking with parent-document retrieval ([#226](https://github.com/sandovaldavid/kioku/issues/226)) ([a0753d4](https://github.com/sandovaldavid/kioku/commit/a0753d4945df1330a89d14dc5932ab8be3d57428))
* **server:** retrieval quality eval harness, model prefixes and BM25 search ([#225](https://github.com/sandovaldavid/kioku/issues/225)) ([a73a6a6](https://github.com/sandovaldavid/kioku/commit/a73a6a68fcb46d4947ee9c273d5a1691cb78df20))
* **server:** support grouped/nested project identifiers ([#229](https://github.com/sandovaldavid/kioku/issues/229)) ([e9e74c0](https://github.com/sandovaldavid/kioku/commit/e9e74c007d5950863b8d9f651a881298f4a9bd98))
* **server:** Templater bridge interop, template management tools and richer frontmatter ([#228](https://github.com/sandovaldavid/kioku/issues/228)) ([4128612](https://github.com/sandovaldavid/kioku/commit/4128612364aa51e9c034a7bc5a967b23367b9456))


### Bug Fixes

* **server:** field-test bugs, skill update, and token reduction ([#232](https://github.com/sandovaldavid/kioku/issues/232)) ([e829345](https://github.com/sandovaldavid/kioku/commit/e8293450c744f32c3ec8f11dfa43a91d3974857b))
* **server:** reindex immediately after create_note_from_template ([#231](https://github.com/sandovaldavid/kioku/issues/231)) ([2fdb823](https://github.com/sandovaldavid/kioku/commit/2fdb82344583b5f85de57f60789e8f9d813a4b53))


### Performance Improvements

* **server:** document real tool costs and trim verbose descriptions ([#233](https://github.com/sandovaldavid/kioku/issues/233)) ([3692d3b](https://github.com/sandovaldavid/kioku/commit/3692d3b682c90392546f483ccf9e347ad200de08))

## [2.2.1](https://github.com/sandovaldavid/kioku/compare/v2.2.0...v2.2.1) (2026-07-11)


### Bug Fixes

* **docs:** center home layout, add mermaid support and code syntax highlighting ([#219](https://github.com/sandovaldavid/kioku/issues/219)) ([16cc181](https://github.com/sandovaldavid/kioku/commit/16cc181ae29a3e6f66f41aa31c0de0280b2c9da8))

## [2.2.0](https://github.com/sandovaldavid/kioku/compare/v2.1.1...v2.2.0) (2026-07-11)


### Features

* **integrations:** one-command MCP registration for 4 AI coding CLIs ([#214](https://github.com/sandovaldavid/kioku/issues/214)) ([fae03b2](https://github.com/sandovaldavid/kioku/commit/fae03b2b1a1b642b8d9a526e026891b73b963b41))

## [2.1.1](https://github.com/sandovaldavid/kioku/compare/v2.1.0...v2.1.1) (2026-07-10)


### ⚠ BREAKING CHANGES

* **release:** the MCP tool suggest_tags in NoteQueryTools (core, always registered) is renamed to inspect_note_tags. Agents calling the read-only diagnostic variant by name must update to inspect_note_tags.

### Miscellaneous Chores

* **release:** catch develop up to main (2.1.0) ([#205](https://github.com/sandovaldavid/kioku/issues/205)) ([04a0757](https://github.com/sandovaldavid/kioku/commit/04a07573534487b224f0c8bfc38482ff0471db35))
* **release:** force 2.1.1 to absorb a poisoned history commit ([197d5e2](https://github.com/sandovaldavid/kioku/commit/197d5e2302c159b89886e53fb3565b7f69cda299))

## [2.1.0](https://github.com/sandovaldavid/kioku/compare/v2.1.0-beta.1...v2.1.0) (2026-07-10)


### Miscellaneous Chores

* **release:** force stable 2.1.0 release ([#200](https://github.com/sandovaldavid/kioku/issues/200)) ([8480ebf](https://github.com/sandovaldavid/kioku/commit/8480ebf046a150627ed77fe239403fca28640c47))

## [2.1.0-beta.1](https://github.com/sandovaldavid/kioku/compare/v2.0.2...v2.1.0-beta.1) (2026-07-10)


### ⚠ BREAKING CHANGES

* **release:** the MCP tool suggest_tags in NoteQueryTools (core, always registered) is renamed to inspect_note_tags. Agents calling the read-only diagnostic variant by name must update to inspect_note_tags.

### Bug Fixes

* **release:** revert spurious v3.0.0-beta version bump ([#197](https://github.com/sandovaldavid/kioku/issues/197)) ([bbb4d72](https://github.com/sandovaldavid/kioku/commit/bbb4d721f49091755f8ffa8ae1e09320f4543443))


### Miscellaneous Chores

* **release:** consolidate to single release-please channel on main ([#195](https://github.com/sandovaldavid/kioku/issues/195)) ([50df4d2](https://github.com/sandovaldavid/kioku/commit/50df4d252a29cb0732535a3d09438f81468f8762))

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
