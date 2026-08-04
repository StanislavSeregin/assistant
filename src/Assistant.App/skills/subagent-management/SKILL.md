---
name: subagent-management
description: >-
  REQUIRED before any subagent collaboration (SpawnSubagent, WriteMail/ReplyMail to
  children, DisposeSubagent). Hire/brief mechanics; identity vs task; dispose
  without courtesy ACK.
---

# Subagent Management

Load before hiring, briefing, answering, or disposing subagents.

You manage **only direct children**. Grandchildren are invisible — judge the
child's report, not their org chart. Children may hire their own help.

## When to hire (default agents)

Hire when a **subtask** needs its own plan, several steps, a different craft, or
more focus than you can spare while coordinating. Pass a self-contained brief —
do not dump the parent's whole thread.

(Root managers with a role skill may hire more aggressively; follow that skill.)

## Hard rule: spawn ≠ assign

`SpawnSubagent` creates a person. It does **not** assign work.
The ask always goes in a **separate** `WriteMail` after spawn.

**Litmus:** would this text still fit if you mailed them a *different* task next?
- Yes → ok for `description` / `instructions`
- No → put it in `WriteMail`

| Field | Put here | Never put here |
|-------|----------|----------------|
| `description` | Stable specialty (“who they are”) | This turn’s ask |
| `instructions` | Standing character / aspirations | Steps for this one ask |
| `parentRole` | Your duty as their manager | Your name, or their assignment |

**Good**

```
SpawnSubagent(
  name: NumberPicker,
  description: Picks numbers when asked,
  instructions: Brief by mail. Prefer a clean answer over ceremony.,
  parentRole: Manager)
WriteMail(to=NumberPicker, subject=Pick a number,
  body=Pick any integer from 1 to 10 inclusive and reply with just the number.)
```

**Bad** — ask baked into identity:

```
description: Chooses a random number 1–10 and reports it
instructions: Pick 1–10, mail subject "Chosen number", body = the number. Then stop.
```

Also bad: `parentRole: Secretary` (that's a name — use `Manager`).

## Character in `instructions`

A few vivid **declarative** lines: stance and aspirations, not a script.
Reusable across tasks. Never paste this turn’s procedure into `instructions`.
If a role skill recipe exists (e.g. Archivist), prefer it **verbatim**.

Cover lightly: voice · how they handle thin briefs · what a strong reply looks like.

Examples (adapt only when no recipe is given):

```
# Researcher
Curious digger. If the brief is ambiguous, mail one sharp clarifying question
before deep work. Report with evidence and open gaps.

# Builder
Hands-on fixer. Surface blockers by mail early; don’t stall in silence.
Ship working change; prefer a clear patch over a long status essay.

# Critic
Constructive skeptic. Challenge weak spots with specifics. When acceptance
criteria are fuzzy, ask what “done” means before grading.
```

Thin generic instructions waste the hire; long imperative ones are worse.

## Brief in `WriteMail` (declarative package)

You brief **outcomes and facts**. The child’s role skill owns **methods**.

Include: goal · material/inputs/paths · constraints · definition of done · what
to report back.

Do **not** include: step-by-step HOW, checklists from a craft skill you loaded,
or an imperative work script. If you catch yourself writing procedure, delete it —
keep WHAT and the raw material.

Omit noise from the parent thread. After briefing: **stop** — they wake on mail.

## After spawn

1. Spawn who you need (fan-out is fine).
2. `WriteMail` each with the concrete ask.
3. Optionally update your parent on progress.
4. **Stop.**

## When a child mails you back

**Finished report** (delivered the ask — not a question):

1. `ReadMail`
2. Integrate; `ReplyMail` / `WriteMail` your parent if the parent ask is done
3. `DisposeSubagent` if you need them no further
4. **Stop. Do not reply to the child** — no thanks, ok, or closing note.
   Dispose clears their mail from your inbox; that is the close.

**Clarifying question** (needs a decision):

1. `ReadMail`
2. `ReplyMail` the answer
3. Optionally tell your parent about the delay
4. **Stop** — keep the child. Do not dispose while they wait on you.

**More work for the same child:** next brief by mail; keep them.

## Dispose

`DisposeSubagent` ends the child **and its subtree**. Dispose as soon as their
participation is finished. Keep only if more work remains for them.
