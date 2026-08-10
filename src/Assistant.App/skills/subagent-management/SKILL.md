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

After briefing — **STOP** (they wake on mail).

## Loop

1. Spawn who you need (fan-out ok).
2. `WriteMail` each.
3. Optionally short progress to parent.
4. **STOP.**

**Finished report** (delivered the ask, not a question): ReadMail → integrate →
parent if their ask is done → `DisposeSubagent` if needed → **STOP**.
Do not ReplyMail the child (dispose clears their mail from your inbox).

**Clarifying question:** ReadMail → `ReplyMail` the answer → optional delay note
to parent → **STOP** (do not dispose while they wait on you).

More work for the same child — next brief by mail.

## Dispose

`DisposeSubagent` ends the child **and its subtree**. Dispose as soon as their
participation is finished.
