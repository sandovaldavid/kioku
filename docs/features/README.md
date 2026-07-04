# Kioku — Feature Specs

Technical specifications for the next wave of features for the MCP server and the Obsidian
plugin. Each spec defines motivation, design, affected files, and risks. The work breakdown
(branches, priorities, acceptance criteria) lives in [`docs/tasks/`](../tasks/README.md).

Context: the project currently has **102 MCP tools across 17 classes**
([commands-reference.md](../commands-reference.md)) and a plugin with **22 bridge
commands**. These specs are derived from the 2026-06-27 review ([docs/review/](../review/README.md)),
the persona roadmap ([05-feature-roadmap.md](../review/05-feature-roadmap.md)), and the
gaps identified in the 2026-07-02 review.

| # | Spec | Area | Task | Impact | Effort |
|---|------|------|-------|:-------:|:--------:|
| 01 | [Latent bridge tools](01-bridge-latent-tools.md) | server | [P1-01](../tasks/P1-01-bridge-latent-tools.md) | ★★ | S |
| 02 | [Wikilink auto-update](02-wikilink-auto-update.md) | server | [P1-02](../tasks/P1-02-wikilink-auto-update.md) | ★★★ | M |
| 03 | [Plugin status UI](03-plugin-status-ui.md) | plugin | [P1-03](../tasks/P1-03-plugin-status-ui.md) | ★★ | S |
| 04 | [WebSocket bridge auth](04-bridge-auth-token.md) | server + plugin | [P1-04](../tasks/P1-04-bridge-auth-token.md) | ★★ | M |
| 05 | [Local generation (Ollama)](05-local-generation.md) | server | [P2-01](../tasks/P2-01-local-generation.md) | ★★★ | M |
| 06 | [Link suggestions](06-link-suggestions.md) | server | [P2-02](../tasks/P2-02-link-suggestions.md) | ★★★ | M |
| 07 | [Daily digest](07-daily-digest.md) | server | [P2-03](../tasks/P2-03-daily-digest.md) | ★★★ | S |
| 08 | [Smart inbox](08-smart-inbox.md) | server | [P2-04](../tasks/P2-04-smart-inbox.md) | ★★ | S |
| 09 | [MCP Prompts & Resources](09-mcp-prompts-resources.md) | server | [P2-05](../tasks/P2-05-mcp-prompts-resources.md) | ★★★ | M |
| 10 | [Zotero / BibTeX](10-zotero-bibtex.md) | server | [P3-01](../tasks/P3-01-zotero-bibtex.md) | ★★★ | M |
| 11 | [Flashcards / Anki](11-flashcards.md) | server | [P3-02](../tasks/P3-02-flashcards.md) | ★★★ | M |
| 12 | [Incremental re-embedding](12-incremental-reembedding.md) | server | [P3-03](../tasks/P3-03-incremental-reembedding.md) | ★★★ | M |
| 13 | [Citation graph](13-citation-graph.md) | server | [P3-04](../tasks/P3-04-citation-graph.md) | ★★ | M |

> Impact: ★ (nice) → ★★★ (killer feature) · Effort: S / M / L

## Design principle

Kioku offloads cheap/repetitive knowledge work to a **local model** (Ollama) to save
cloud agent tokens and protect privacy. Every feature should ask itself:
*can this be solved with embeddings + a small local model, degrading gracefully if
Ollama is unavailable?* Spec 05 (local generation) is the **enabler** for 07, 11, and
future synthesis features — prioritize it first within P2.
