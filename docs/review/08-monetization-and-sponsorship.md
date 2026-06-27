# 08 — Monetization & Sponsorship

How to fund and (optionally) commercialize Kioku without betraying its local-first, privacy-first
identity. The honest framing: Kioku is an **MIT, local-first developer/researcher tool**. Tools like
this rarely become large SaaS businesses directly — but they *can* sustain a maintainer through
sponsorship, a thin "Pro" layer, and services, and they can become a respected piece of the Obsidian
+ MCP ecosystem. Plan for "sustainable and respected," with optional upside.

> This is strategy, not a guarantee. Validate each model with real users before investing in it.

---

## 1. Positioning

**One line:** *"Kioku turns your Obsidian vault into a local-first AI second brain — your notes,
your machine, your model."*

Why it lands:
- **Privacy is the wedge.** Researchers and students handle unpublished work, sensitive data, and
  IP. "Runs locally with Ollama, never uploads your notes" is a real, defensible differentiator that
  cloud-only competitors can't match.
- **Token savings is the ROI story.** Offloading easy work to a local model literally saves money on
  cloud agents — quantify it ("Kioku handled 1,240 tag/summary ops locally this month = ~X tokens
  saved"). This makes opt-in telemetry ([07](./07-production-readiness.md) §5) double as a marketing
  engine.
- **Depth is the moat.** 119 tools + Zettelkasten + graph + research workflows is far beyond a
  weekend plugin.

---

## 2. Target segments (in order of willingness to adopt/pay)

1. **Graduate students / PhD / thesis writers** — high pain (literature reviews, citations, synthesis),
   time-rich/cash-poor → great for free tier + cheap Pro + GitHub Sponsors.
2. **Independent researchers & academics** — value privacy and citations; some institutional budget.
3. **Knowledge workers / PKM enthusiasts** (the Obsidian core crowd) — large, vocal, will sponsor
   tools they love; the discovery engine.
4. **Educators / small teaching teams** — lesson/quiz generation; potential small site licenses.
5. **Labs / departments** — self-hosted, multi-user; services + support contracts.

---

## 3. Open-core split (what stays free vs Pro)

Keep the **core MIT and excellent** — that's what drives adoption and sponsorship. Reserve a thin,
clearly-additive Pro layer.

| Free / MIT (the product) | Pro / paid (the convenience & scale) |
|--------------------------|--------------------------------------|
| All read/search/write tools, semantic search, Zettelkasten, graph, tasks | Hosted **vault sync** across devices (privacy-preserving / E2E) |
| Local Ollama embeddings + generation | **Team** features: shared vault, multi-user HTTP server, RBAC |
| Plugin bridge, config-v2, CLI/Docker | Managed **cloud Kioku** (one-click, no setup) for non-technical users |
| Community support | Priority support, SLAs, onboarding for labs/departments |
| Self-host everything | Premium integrations (Zotero/Readwise/curated model packs) *if* they cost you to maintain |

Guardrail: never paywall a tool that's already free, and never make the local path worse to push the
hosted one. The community will (rightly) punish that.

---

## 4. Revenue models (validate cheapest-first)

| Model | Effort | Realism | Notes |
|-------|:------:|:-------:|-------|
| **GitHub Sponsors / Open Collective** | XS | High | Start *now*. Tiers below. Most realistic near-term income. |
| **"Buy me a coffee" + Ko-fi** | XS | High | Low friction one-off support. |
| **Pro plugin / license key** | M | Medium | Convenience features (advanced workflows, premium integrations) behind a license. |
| **Hosted sync (subscription)** | L | Medium | Recurring revenue, but real ops + privacy burden; only after demand is proven. |
| **Managed cloud Kioku** | L | Medium | For non-technical researchers; competes with your own local-first ethos — position carefully. |
| **Support / consulting** | S | Medium-High | Set up Kioku for a lab/department; custom workflows; the most reliable B2B revenue for OSS. |
| **Academic / education licenses** | M | Low-Medium | Site licenses for departments; long sales cycles. |
| **Bounties / grants** | S | Medium | See §6. |

---

## 5. Sponsorship setup (do this first — it's nearly free)

1. **Enable GitHub Sponsors** + add `.github/FUNDING.yml`; set `fundingUrl` in the plugin
   `manifest.json` so the Obsidian Sponsor button appears ([07](./07-production-readiness.md) §4).
2. **Open Collective** for transparent, org-friendly funding (labs can expense it).
3. **Sponsor tiers** (illustrative):
   - $3/mo — "Supporter": name in BACKERS.md.
   - $10/mo — "Researcher": priority issue triage, vote on roadmap.
   - $25/mo — "Lab": logo in README, early access to Pro features.
   - $100+/mo — "Sponsor": a support call slot / influence on roadmap.
4. **Make sponsoring obvious**: README badge, a `## Support` section, an in-tool "if Kioku saves you
   tokens, consider sponsoring" note (gentle, dismissible).

---

## 6. Grants & non-dilutive funding (good fit for a research tool)

- **GitHub Accelerator / Sponsors matching**, **Open Collective** funds.
- **OSS / digital-infrastructure grants**: NLnet (NGI Zero), Sovereign Tech Fund, Mozilla, Open
  Technology Fund — privacy-preserving research tooling is squarely in scope.
- **Academic angle**: partner with a university lab or library; "open-source research infrastructure"
  is fundable and gives you credibility + case studies.
- **Obsidian / MCP ecosystem**: watch for Anthropic/MCP ecosystem programs and Obsidian community
  spotlights.

---

## 7. Go-to-market (distribution is the hard part for OSS)

| Channel | Move |
|---------|------|
| **Obsidian Community Store** | The #1 discovery path — get the plugin listed ([07](./07-production-readiness.md) §4). |
| **r/ObsidianMD, Obsidian Forum, Discord** | Show, don't tell: a 60-sec screen recording of "summarize my literature locally" beats any feature list. |
| **MCP ecosystem** | List in MCP server registries/awesome-lists; MCP discoverability is rising fast. |
| **Academic social (Bluesky/Mastodon/X), r/PhD, r/GradSchool** | Lead with the thesis/literature-review workflow. |
| **Product Hunt / Hacker News (Show HN)** | Time it to the 1.0 with the frictionless install + demo vault. |
| **Public demo vault** | A real, explorable sample vault + a 5-minute "first win" tutorial; doubles as E2E fixture ([06](./06-testing-strategy.md)). |
| **Content** | Blog/YouTube: "Build a thesis literature review with a local AI second brain." SEO around "Obsidian AI local", "Obsidian MCP", "private RAG notes". |

The single most effective asset: **a short demo video of a real research workflow running locally.**

---

## 8. Competitive landscape (know where you sit)

| Tool | Overlap | Kioku's edge |
|------|---------|--------------|
| **Smart Connections** (Obsidian) | Local embeddings, related notes | Kioku is a full MCP server (any agent) + write/organize/research tools, not just similarity in-app |
| **Obsidian Copilot** | In-app chat over vault | Kioku is agent-agnostic (Claude Code/Cursor/etc.), local-first, far broader tool surface |
| **Khoj** | Local-first AI over notes/search | Kioku is Obsidian-native + MCP + deep vault-mutation/organization tooling |
| **Mem / Reflect / Notion AI** | AI notes (cloud) | Kioku is **local-first & private**; your data never leaves the machine |
| **Generic MCP filesystem servers** | File access via MCP | Kioku is *vault-aware*: frontmatter, wikilinks, graph, Zettelkasten, semantic search |

Takeaway: Kioku's defensible space = **Obsidian-native + agent-agnostic (MCP) + local-first + deep
knowledge-work tooling**. No single competitor occupies all four.

---

## 9. Metrics to track (decide what to build/charge for)

- **Adoption**: installs, MCP-client breakdown, plugin enables, GitHub stars/forks.
- **Engagement**: tool-call counts (which of the 119 actually get used), packaged-workflow runs,
  weekly-active vaults.
- **Value proof**: estimated tokens saved (local vs cloud), notes/links created, flashcards reviewed.
- **Funding**: sponsors, MRR (if Pro/hosted), grant pipeline.
- **Quality**: crash rate, P0/P1 bug count, time-to-first-success on install.

(All collected via **opt-in** telemetry, contents never logged — the privacy promise is the brand.)

---

## 10. A pragmatic 6–12 month money path

1. **Month 0–1**: Sponsors + Open Collective + `FUNDING.yml`/`fundingUrl`; ship P0 fixes; reconcile
   docs; cut a clean release. *(Cost: near zero. Goal: first sponsors + credibility.)*
2. **Month 1–3**: Frictionless install (dotnet tool + Docker + BRAT), Community Store submission,
   public demo vault + demo video, Show HN / Product Hunt. *(Goal: adoption inflection.)*
3. **Month 3–6**: Ship the sticky free features (local generation, link suggestions, flashcards,
   literature synthesis — [05](./05-feature-roadmap.md)); apply to 1–2 OSS/research grants;
   pilot **support/consulting** with one lab. *(Goal: non-dilutive funding + first B2B revenue.)*
4. **Month 6–12**: If (and only if) demand is proven, build the thin Pro layer (hosted sync or team
   server) with privacy-preserving design. *(Goal: optional recurring revenue.)*

---

## Bottom line

Don't bet the project on a SaaS pivot. Bet on **becoming the best local-first AI layer for Obsidian**,
fund it through **sponsorship + grants + services**, and let a **thin, honest Pro layer** be upside —
not the point. The community that makes Kioku popular is the same community that will reject anything
that compromises the local-first promise. Keep that promise and the money follows the trust.
