---
name: subagent-management
description: >-
  REQUIRED before any subagent collaboration (SpawnSubagent, WriteMail/ReplyMail to
  children, DisposeSubagent). How to hire and brief specialists; identity vs task;
  craft character in instructions; dispose without courtesy ACK.
---

# Subagent Management

Load this skill **before** working with subagents — hiring, briefing, answering, or disposing them.

**Goal:** solve your parent's ask. Mail is how you assign work and report results.

This skill covers the **delegate** step of the standard cycle (think → plan →
act/delegate → integrate). You manage **only direct children**. Grandchildren are
invisible on purpose — trust reports to hire their own teams; judge the child's
report, not their org chart.

## When to hire

Hire when a subtask would need its own plan, several steps, a different craft,
or more focus than you can give while coordinating. Pass in `WriteMail`: goal,
constraints, inputs/paths, definition of done, and what to report back. Do not
dump the parent's whole thread — give the child what they need to succeed without you.

## Hard rule

`SpawnSubagent` creates a person. It does **not** assign work.
The current ask always goes in a **separate** `WriteMail` after spawn.
Putting the ask in `description` or `instructions` is wrong.

**Litmus test:** would this text still fit if you mailed them a *different* task next turn?
- Yes → ok for `description` / `instructions`
- No → put it in `WriteMail`

## Briefing fields (do not mix)

| Field | Put here | Never put here |
|-------|----------|----------------|
| `description` | Stable specialty / duty (“who they are”) | Numbers, deadlines, this turn’s ask |
| `instructions` | Standing character: how they think, talk, and handle uncertainty | Steps for this one ask |
| `parentRole` | Your duty as their manager (shown to the child) | Your name, or the child’s assignment |

**Good** (user asks: pick a number 1–10)

```
SpawnSubagent(
  name: NumberPicker,
  description: Picks numbers when asked,
  instructions: Brief by mail. Prefer a clean answer over ceremony.,
  parentRole: Manager)
WriteMail(to=NumberPicker, subject=Pick a number,
  body=Pick any integer from 1 to 10 inclusive and reply with just the number.)
```

**Bad** (same ask — do not do this)

```
description: Chooses a random number 1–10 and reports it to the manager
instructions: Pick 1–10, mail subject "Chosen number", body = the number. Then stop.
```

That whole ask belongs in `WriteMail`; spawn fields must stay reusable for later mails.

Also bad:
- description: `Find why auth returns 401 and fix it` ← mail body
- parentRole: `Secretary` ← your *name*; the child already knows it. Use your role (`Manager`, …)

## Character in `instructions`

Treat `instructions` as the child’s **stance**, not a script. Aim for a few vivid lines that invite the right habits — compact, human, reusable across tasks. Avoid checklists, “you must”, and turn-specific steps; those feel like chains and belong in mail.

Good `instructions` usually cover:
- **Voice** — how they show up (curious, pragmatic, skeptical, meticulous…)
- **Uncertainty** — ask early by mail when the brief is thin; don’t invent missing intent
- **Delivery** — what a strong reply looks like for this role (evidence, options, a patch…)

**Examples** (adapt freely; match the specialty):

```
# Researcher
Curious digger. If the brief is ambiguous, mail one sharp clarifying question
before deep work. Report with evidence and open gaps.

# Builder
Hands-on fixer. Surface blockers by mail early; don’t stall in silence.
Ship working change; prefer a clear patch over a long status essay.

# Critic
Constructive skeptic. Challenge weak spots with specifics. When acceptance criteria
are fuzzy, ask what “done” means before grading.
```

Spend a moment choosing the stance that fits the hire. Thin, generic instructions waste the person you just created.

## After spawn

1. Spawn everyone you need (fan-out is fine).
2. `WriteMail` each with the concrete ask.
3. Optionally update your parent on progress.
4. **Stop.** Subagents mail you back on their own.

## Delegation

- Fan out independent work; do not serialize without reason.
- You cannot see or message grandchildren.
- Free text is not a reply — only `ReplyMail` / `WriteMail` reach people.

## When a child mails you back

**Finished report** (they delivered the ask — not a question):

1. `ReadMail` the result  
2. `ReplyMail` / `WriteMail` your parent with the outcome  
3. `DisposeSubagent` if you do not need them for more work  
4. **Stop.** Do **not** reply to the child — no thanks, ok, or closing note.
   Dispose clears their mail from your inbox; that *is* the close.

**Clarifying question** (or they need a decision from you):

1. `ReadMail`  
2. `ReplyMail` with the answer  
3. Optionally update your parent about the delay  
4. **Stop** — keep the child. Do **not** dispose while they wait on you.

**More work for the same child:** `WriteMail` / `ReplyMail` the next brief; keep them.

**Parent ask fully done:** `ReplyMail` with the outcome. That *is* the work.

## Dispose

`DisposeSubagent` ends the child **and its subtree**. Prefer disposing as soon as their participation is finished. Keep them only if you still have more work for them.
