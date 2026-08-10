---
name: subagent-management
description: >-
  REQUIRED before SpawnSubagent, WriteMail/ReplyMail to children, DisposeSubagent.
  Hire/brief/dispose mechanics only.
---

# Subagent Management

Load before hiring, briefing, answering a child, or disposing.

You manage **direct children only**. Grandchildren are not addressable — judge the
child's report.

## When to hire

When a subtask needs its own plan, several steps, another craft, or focus you
cannot spare while coordinating. Brief must be self-contained — do not dump the
parent thread.

For **your own** multi-step craft: put the plan on `Checklist*` and continue across
episodes. Hire when you need another craft, true parallel independent slices, or
focus you cannot spare while coordinating.

(Role skills may hire more strictly — follow them.)

## Spawn ≠ assign

`SpawnSubagent` creates a person. The ask is a separate `WriteMail`.

| Field | Put here | Never here |
|-------|----------|------------|
| `description` | stable specialty | this turn's ask |
| `instructions` | character / stance | steps for one ask |
| `parentRole` | your duty (`Manager`) | your name or their ask |

**Litmus:** would this text still fit if you mailed a *different* task next?
Yes → identity. No → mail.

```
SpawnSubagent(
  name: NumberPicker,
  description: Picks numbers when asked,
  instructions: Brief by mail. Prefer a clean answer over ceremony.,
  parentRole: Manager)
WriteMail(to=NumberPicker, subject=Pick a number,
  body=Pick any integer from 1 to 10 inclusive and reply with just the number.)
```

Role recipes (Archivist, Digest, …) live in the parent's **role skill** — prefer verbatim.

`instructions`: a few declarative lines (voice, thin briefs, what a strong reply looks like).
Not a task script.

## Brief (`WriteMail`)

WHAT: goal · material/paths · constraints · done · what to report.  
Not HOW: no step-by-step procedures or checklists from a craft skill you must not load.

After briefing — end the episode (they wake on mail).

## Loop

1. Spawn who you need (fan-out ok).
2. `WriteMail` each.
3. Optionally short progress to parent (`WriteMail`).
4. End the episode.

### Finished report from a child

Delivered the ask (not a question): `ReadMail` → integrate → reply/report to
**your parent** if their ask is done → `DisposeSubagent` → end episode.

**No `ReplyMail` to the child** on a finished report. Dispose removes their mail
from your inbox; a courtesy or “thanks” reply only spawns useless new work.

### Clarifying question from a child

`ReadMail` → `ReplyMail` the answer → optional delay note to parent → end episode  
(keep them; do not dispose while they wait on you).

More work for the same child — next brief by mail (`WriteMail`), not a reply to
an already-finished report.

## Dispose

`DisposeSubagent` ends the child **and its subtree**. Dispose as soon as their
participation is finished.
