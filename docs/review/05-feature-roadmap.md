# 05 — Feature Roadmap & Integrations

What to build so Kioku becomes a daily driver for **research (thesis, papers)**, **study/learning**,
and **teaching** — the "second brain" use cases. Features are organized by persona, then by
cross-cutting platform bets. Each item notes how it builds on what already exists (Kioku already has
119 tools incl. Zettelkasten, tasks, graph, research, sessions, CSS, git).

> Effort: XS / S / M / L · Impact: ★ (nice) → ★★★ (killer feature)

---

## Design principle: keep the local model doing the cheap work

Kioku's thesis is "offload easy/repetitive knowledge-work to a **local** model to save tokens and
protect privacy." Every feature below should ask: *can Ollama (embeddings + a small local
generation model) do this, falling back to the cloud agent only when it can't?* That framing is both
the product moat and the marketing story.

**Enabler (do this first):** add a **local generation** path alongside embeddings — Kioku already
talks to Ollama for `nomic-embed-text`; add an optional `KIOKU_GEN_MODEL` (e.g. `llama3.2`,
`qwen2.5`) so summarize/tag/classify/flashcard tools can run entirely locally. Impact ★★★, Effort M.
This single capability multiplies the value of half the features below.

---

## Persona 1 — Researcher / thesis / scientific papers

| Feature | What it does | Builds on | Impact | Effort |
|---------|--------------|-----------|:------:|:------:|
| **Zotero / BibTeX bridge** | Import a Zotero library or `.bib`, create literature notes with metadata, keep citation keys in frontmatter | `create_literature_note`, `export_citations`, config-v2 templates | ★★★ | M |
| **PDF & annotation ingest** | Pull highlights/annotations from PDFs (or the Obsidian PDF++/Annotator plugins) into literature notes | bridge + `ResearchTools` | ★★★ | L |
| **Literature-review synthesis** | "Summarize what my notes say about X, grouped by theme, with citations" — local-first synthesis grounded in the vault | semantic search + local gen | ★★★ | M |
| **Literature-gap analysis** | Already have `get_literature_gap` — extend to cluster topics and flag under-cited / orphan claims | `get_literature_gap`, graph tools | ★★ | M |
| **Citation graph** | Build a graph of which notes cite which sources; find the most-cited and the isolated | `get_backlinks`, graph analysis | ★★ | M |
| **Claim → evidence linking** | For each assertion in a draft, surface supporting/contradicting notes | semantic search | ★★ | M |
| **Argument/outline scaffolder** | Turn a cluster of notes into a thesis-chapter outline with linked evidence | MOC tools + local gen | ★★ | M |
| **Reference manager export** | Export a reading list / bibliography in BibTeX/CSL-JSON/APA/IEEE | `export_citations` | ★★ | S |
| **Duplicate / near-duplicate detection** | Semantic dedup of notes & sources (beyond `find_duplicate_notes`) | embeddings | ★ | S |

---

## Persona 2 — Student / self-learner

| Feature | What it does | Builds on | Impact | Effort |
|---------|--------------|-----------|:------:|:------:|
| **Spaced-repetition / flashcards** | Generate Q/A or cloze cards from notes; export to Anki (`.apkg`/CSV) or the Spaced Repetition plugin format | local gen + bridge | ★★★ | M |
| **Daily review digest** | "What did I learn this week / what's due for review / what's unlinked" — a generated daily note | `get_recent_activity`, `get_knowledge_timeline`, tasks | ★★★ | S |
| **Socratic tutor mode** | An agent prompt/tool that quizzes you over your own vault and explains gaps, grounded in your notes | semantic search + local gen | ★★★ | M |
| **Concept-map auto-build** | Generate/refresh a concept map (MOC) for a topic from related notes | `get_concept_map`, MOC tools | ★★ | M |
| **"Explain like I'm 5 / 25"** | Re-explain a note at a chosen level, locally | local gen | ★★ | S |
| **Smart inbox processing** | `process_inbox` exists — extend to auto-suggest folder/tags/links and queue for one-click apply | `process_inbox`, `suggest_tags`, `suggest_folder` | ★★ | S |
| **Progress / streak tracking** | Study streaks, notes-created, cards-reviewed over time | sessions + timeline | ★ | S |

---

## Persona 3 — Teacher / educator

| Feature | What it does | Builds on | Impact | Effort |
|---------|--------------|-----------|:------:|:------:|
| **Lesson / syllabus generator** | Turn a topic cluster into a lesson plan or syllabus with linked readings | MOC + local gen | ★★ | M |
| **Quiz/exam generator** | Produce question banks (MCQ/short-answer) with answer keys from notes | local gen | ★★ | M |
| **Handout / slide export** | Export a note or MOC to a clean handout (Markdown→PDF) or slide outline (Marp/reveal) | `ResearchTools` HTML export | ★★ | M |
| **Live lecture capture tools** | Plugin commands to insert callouts/timestamps/sections at cursor during a class | plugin handlers | ★ | S |
| **Reading-list assembler** | Build and share a curated, linked reading list for students | links + export | ★ | S |

---

## Cross-cutting "second brain" graph features

The vault's value is the *graph* — Kioku should actively strengthen it.

| Feature | What it does | Builds on | Impact | Effort |
|---------|--------------|-----------|:------:|:------:|
| **Link suggestions** | "These 5 notes should probably link to each other" via semantic similarity; one-click to add `[[wikilinks]]` | embeddings, `link_related_notes` | ★★★ | M |
| **Bridge the islands** | `find_graph_islands` exists — add "suggest the bridge note/link that would connect this island" | graph analysis + embeddings | ★★★ | M |
| **Orphan rescue** | Surface orphan notes/assets and propose homes (folder/tag/links) | `find_unlinked_notes`, `find_orphan_assets` | ★★ | S |
| **Vault health report** | A scheduled "state of your brain" report: density, growth, orphans, stale notes, broken links | `audit_vault`, `measure_vault_density` | ★★ | S |
| **Stale-note nudges** | Flag notes untouched for N months that are highly connected (worth revisiting) | timeline + graph | ★ | S |

---

## Platform & integration bets

| Bet | Why | Impact | Effort |
|-----|-----|:------:|:------:|
| **Model-swappable embeddings & generation** | Let users pick Ollama models per task; map model→dim (ties to BUG-3). Enables quality/speed trade-offs | ★★★ | M |
| **Incremental re-embedding** | Only re-embed changed notes; batch via Ollama; show progress | ★★★ | M |
| **Hybrid re-rank** | Add a cross-encoder/local re-rank stage on top of RRF for better top-K | ★★ | M |
| **MCP Resources & Prompts** | Expose vault notes as MCP *resources* and ship curated MCP *prompts* (e.g. "literature review", "exam from notes") so any MCP client gets turnkey workflows | ★★★ | M |
| **Webhooks / automations** | "On note created in `Inbox/`, run process_inbox" — local automation without the cloud agent | ★★ | M |
| **Mobile read-only** | Lightweight read/search over the vault on mobile (Obsidian mobile + a hosted read endpoint) | ★★ | L |
| **Multi-vault** | Manage several vaults (work/personal/thesis) from one server | ★ | M |
| **Zotero + Readwise + Hypothes.is** | Inbound research sources beyond Zotero | ★★ | L |

---

## Packaged workflows (the demo-able magic)

These combine existing tools into one-call experiences — great for demos, onboarding, and the
"saves you tokens" story. Ship them as MCP prompts and/or `WorkflowTools`:

1. **`research_digest`** — fetch this week's reading, summarize per source locally, list open
   questions and gaps.
2. **`thesis_chapter`** — given a topic, gather evidence notes, propose an outline, draft section
   stubs with citations.
3. **`study_session`** — pick due flashcards + weak topics, quiz, then log progress to the daily note.
4. **`lesson_from_topic`** — assemble a lesson plan + quiz + reading list from a topic cluster.
5. **`sunday_hygiene`** (exists) — extend to a full "state of your brain" report with one-click fixes.

---

## Alignment with `planning.md` v4

`planning.md` already names **Zotero integration**, **Community Store**, **SSE streaming**, and
**Native AOT** as v4. This roadmap keeps those and sequences the highest-leverage additions
(local generation, link-suggestions, flashcards, MCP prompts/resources) ahead of them, because they
are what make Kioku *sticky* for the three personas — and what a sponsor or buyer will actually see.

---

## Suggested 3-horizon plan

| Horizon | Theme | Headline features |
|---------|-------|-------------------|
| **Now (v1.9–2.0)** | Make the core irresistible | Local generation path · link suggestions · daily digest · smart inbox · MCP prompts/resources |
| **Next** | Research depth | Zotero/BibTeX · literature synthesis · flashcards/Anki · citation graph · incremental re-embedding |
| **Later** | Reach & scale | Mobile read · multi-vault · hybrid re-rank · webhooks · teaching suite |
