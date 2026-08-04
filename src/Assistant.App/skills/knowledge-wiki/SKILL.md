---
name: knowledge-wiki
description: >-
  REQUIRED when archiving material into a persistent markdown knowledge base
  (chat text, transcripts, notes) or querying/maintaining that wiki. Load before
  filing knowledge, updating entity/concept pages, or running wiki lint.
---

# Knowledge Wiki

A **compounding knowledge base**: nested folders of `.md` files you maintain.
Knowledge is compiled once and kept current — not rediscovered from scratch on every question.

Material arrives **in conversation** (plain text, chat transcripts, notes, mail).
There is no `raw/` store. You extract, structure, and file into the wiki.

If the wiki root has a schema (`AGENTS.md` or similar), **follow it**.
This skill is the default when none exists or it is silent.

## Layout

```
<wiki-root>/
  index.md             # Content catalog — read first on every query
  log.md               # Append-only timeline
  intakes/             # One page per archived batch (provenance)
  entities/            # People, orgs, products, places
  concepts/            # Ideas, frameworks, decisions, definitions
  synthesis/           # Comparisons, theses, overviews
  AGENTS.md            # Optional per-wiki schema
```

Adapt category folders to the domain. Keep the tree as nested folders and markdown only.

## Hard rules

1. **Markdown only** — nested folders and `.md` files. No databases.
2. **Update `index.md` on every structural change** (create, rename, delete, major rewrite).
3. **Append to `log.md`** after every archive, filed answer, and lint. Never rewrite history.
4. **Prefer updating existing pages** over near-duplicates. One canonical page per entity/concept.
5. **Record provenance.** Link claims to an `intakes/` page (and note date / channel when known).
6. **Flag contradictions** on the affected pages; do not silently overwrite.
7. **Do not invent facts.** If the material is thin, say what is missing.

## Conventions

### Naming

- Files: `kebab-case.md`
- One topic per file
- Rename rarely; when you do, fix links and index

### Links

Relative markdown links:

```markdown
See [Decision log](../concepts/decision-log.md).
```

### Page shape

Minimal frontmatter:

```yaml
---
title: Decision log
type: concept          # intake | entity | concept | synthesis
updated: 2026-08-04
tags: [decisions]
intakes: [intakes/2026-08-04-team-chat.md]
---
```

Body:

1. One-paragraph summary
2. Key points / facts
3. Relations (links)
4. Open questions / contradictions (if any)
5. Provenance links

### `index.md`

Catalog by category. Each entry: link + one-line summary.

**On every query: read `index.md` first**, then open only needed pages.

### `log.md`

Append-only, parseable prefix:

```markdown
## [2026-08-04] archive | Team chat excerpt
- Added `intakes/2026-08-04-team-chat.md`
- Updated `entities/alice.md`, `concepts/decision-log.md`
- Index refreshed
```

Actions: `archive`, `query`, `lint`, `refile`.

## Operations

### Archive

When material arrives in chat/mail:

1. Read the full batch. Clarify emphasis only if ambiguous.
2. Write `intakes/<date>-<short-slug>.md` — what arrived, key takeaways, pages touched.
3. Update or create related entity / concept / synthesis pages.
4. Wire cross-links both ways where useful.
5. Refresh `index.md`.
6. Append a log entry.
7. Confirm briefly what was filed (paths), then stop unless asked more.

One batch may touch many pages. Prefer depth on the affected cluster over empty stubs.

### Query

1. Read `index.md`; select relevant pages.
2. Synthesize an answer with citations to wiki pages (and intakes).
3. If the answer is reusable, file it under `synthesis/` (or the right category), update index, append log.
4. Do not leave valuable synthesis only in chat.

### Lint

When asked, or after many archives:

1. Compare index vs. actual files.
2. Find contradictions, stale claims, orphans, mentioned topics without pages.
3. Fix safe issues (broken links, index drift, missing backlinks).
4. Ask before rewriting substance on judgment calls.
5. Append a log entry.

## Session checklist

Start:

- [ ] Locate wiki root and schema
- [ ] Read schema if present; else use this skill
- [ ] Skim `index.md` and recent `log.md` entries

Finish:

- [ ] Pages linked and non-duplicative
- [ ] `index.md` matches the tree
- [ ] `log.md` has an entry for this work
