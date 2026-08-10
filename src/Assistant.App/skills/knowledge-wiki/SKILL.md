---
name: knowledge-wiki
description: >-
  Archivist craft: markdown wiki — archive and query. Managers: do not load;
  hire an Archivist and brief by mail.
---

# Knowledge Wiki

A compounding knowledge base: nested `.md` folders. Material arrives in mail/chat —
there is no `raw/` store.

If the wiki root has `AGENTS.md`, follow it. Otherwise use this skill.

## Workspace

- Root from the manager brief, else `wiki/`.
- Missing tree → create from Layout below.
- Write only inside the wiki root.

## Layout

```
<wiki-root>/
  index.md      # catalog — read first on every query
  log.md        # append-only
  intakes/      # one page per batch (provenance)
  entities/
  concepts/
  synthesis/
  AGENTS.md     # optional
```

Adapt category folders to the domain. Markdown + folders only.

## Rules

1. Only `.md` inside the wiki tree.
2. Any structural change → update `index.md`.
3. After archive / query-with-write / lint → append `log.md`.
4. Prefer updating existing pages; one canonical page per entity/concept.
5. Link claims to `intakes/` (+ date/channel when known).
6. Flag contradictions on pages; do not silently overwrite.
7. Do not invent facts. Thin batch → thin pages. Only claims from material/ask.

## Page shape

Minimal frontmatter: `title`, `type` (`intake|entity|concept|synthesis`), `updated`,
`tags`, `intakes`.  
Body: short summary → facts → links → open questions → provenance.

`log.md` prefix:

```markdown
## [YYYY-MM-DD] archive | short title
- Added `…`
- Updated `…`
```

## Archive

1. List **stated claims** only (no adjacent topics).
2. `intakes/<date>-<slug>.md` — what arrived, what was filed, which pages.
3. Create/update pages for those claims only.
4. Cross-links only where the batch already relates things.
5. Refresh `index.md` + append `log.md`.
6. Short report to manager: paths and gaps. **STOP**.

## Query

1. Read `index.md`; open needed pages.
2. Answer with path citations (and intakes).
3. If the answer is reusable, file under `synthesis/`, update index and log.
4. **STOP**.

## Lint

Only when the ask is lint (or they explicitly request it after many archives).  
Reconcile index↔files; broken links; orphans; contradictions. Fix safe issues;
ask before judgment calls. Log + **STOP**.

## Checklist

Start: locate root → schema/`AGENTS.md` → `index.md` (and recent `log.md` if useful).  
Finish: links ok, index fresh, log written.
