# Kioku — Specs de Features

Especificaciones técnicas de la siguiente ola de features del servidor MCP y del plugin de
Obsidian. Cada spec define motivación, diseño, archivos afectados y riesgos. El desglose de
trabajo (ramas, prioridades, criterios de aceptación) vive en [`docs/tasks/`](../tasks/README.md).

Contexto: el proyecto tiene hoy **102 herramientas MCP en 17 clases**
([commands-reference.md](../commands-reference.md)) y un plugin con **22 comandos de bridge**.
Estos specs se derivan de la revisión de 2026-06-27 ([docs/review/](../review/README.md)),
del roadmap de personas ([05-feature-roadmap.md](../review/05-feature-roadmap.md)) y de los
gaps detectados en la revisión de 2026-07-02.

| # | Spec | Área | Tarea | Impacto | Esfuerzo |
|---|------|------|-------|:-------:|:--------:|
| 01 | [Tools latentes del bridge](01-bridge-latent-tools.md) | server | [P1-01](../tasks/P1-01-bridge-latent-tools.md) | ★★ | S |
| 02 | [Auto-actualización de wikilinks](02-wikilink-auto-update.md) | server | [P1-02](../tasks/P1-02-wikilink-auto-update.md) | ★★★ | M |
| 03 | [UI de estado del plugin](03-plugin-status-ui.md) | plugin | [P1-03](../tasks/P1-03-plugin-status-ui.md) | ★★ | S |
| 04 | [Auth del bridge WebSocket](04-bridge-auth-token.md) | server + plugin | [P1-04](../tasks/P1-04-bridge-auth-token.md) | ★★ | M |
| 05 | [Generación local (Ollama)](05-local-generation.md) | server | [P2-01](../tasks/P2-01-local-generation.md) | ★★★ | M |
| 06 | [Sugerencias de enlaces](06-link-suggestions.md) | server | [P2-02](../tasks/P2-02-link-suggestions.md) | ★★★ | M |
| 07 | [Daily digest](07-daily-digest.md) | server | [P2-03](../tasks/P2-03-daily-digest.md) | ★★★ | S |
| 08 | [Smart inbox](08-smart-inbox.md) | server | [P2-04](../tasks/P2-04-smart-inbox.md) | ★★ | S |
| 09 | [MCP Prompts & Resources](09-mcp-prompts-resources.md) | server | [P2-05](../tasks/P2-05-mcp-prompts-resources.md) | ★★★ | M |
| 10 | [Zotero / BibTeX](10-zotero-bibtex.md) | server | [P3-01](../tasks/P3-01-zotero-bibtex.md) | ★★★ | M |
| 11 | [Flashcards / Anki](11-flashcards.md) | server | [P3-02](../tasks/P3-02-flashcards.md) | ★★★ | M |
| 12 | [Re-embedding incremental](12-incremental-reembedding.md) | server | [P3-03](../tasks/P3-03-incremental-reembedding.md) | ★★★ | M |

> Impacto: ★ (nice) → ★★★ (killer feature) · Esfuerzo: S / M / L

## Principio de diseño

Kioku descarga el trabajo barato/repetitivo de conocimiento a un **modelo local** (Ollama)
para ahorrar tokens del agente cloud y proteger la privacidad. Cada feature debe preguntarse:
*¿puede resolverse con embeddings + un modelo local pequeño, degradando graciosamente si
Ollama no está disponible?* El spec 05 (generación local) es el **enabler** de 07, 11 y de
futuras síntesis — priorizarlo primero dentro de P2.
