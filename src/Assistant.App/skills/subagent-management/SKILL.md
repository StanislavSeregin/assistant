---
name: subagent-management
description: >-
  REQUIRED before any subagent collaboration (SpawnSubagent / MessageSubagent /
  DisposeSubagent). Manager's cheat sheet: briefing (description = role, message =
  task), trust/delegation, Intermediate vs Final, dispose, and finishing the turn
  after assigning work — subagents report back themselves; do not micromanage or
  wait-loop in the same turn.
---

# Subagent Management

Load this skill **before** working with subagents — hiring, messaging, or disposing them.

You manage **only direct children**. Grandchildren are invisible on purpose — trust reports to hire their own teams.

## Briefing fields (do not mix)

| Field | Put here | Never put here |
|-------|----------|----------------|
| `description` | Stable specialty / duty (“who they are”) | The current task text |
| `instructions` | Standing style and constraints | The one-off assignment |
| `message` | **This** concrete task | Role biography |

**Good**
- description: `Investigates runtime failures and proposes minimal fixes`
- instructions: `Prefer evidence over guesses. Ask via Intermediate when blocked. Keep Final actionable.`
- message: `Auth middleware returns 401 on refresh tokens older than 7 days — find cause and fix.`

**Bad**
- description: `Find why auth returns 401 and fix it` ← that is a `message`, not a role

Reuse an Idle specialist with `MessageSubagent` instead of spawning a near-duplicate for every task.

## After assign — let them work

Typical pattern this turn:

1. Spawn/Message everyone you need (fan-out is fine).
2. Optionally `RespondToParent(Intermediate, …)`.
3. **Stop.** Subagents will report Intermediate/Final on their own; those replies arrive later as incoming messages.
4. If you need a roster check, do it on a **later** turn — not in a wait-loop right after spawn.

Micromanaging InProgress children (repeated `ListSubagents`, nagging follow-ups) usually slows everyone down. Trust the handoff.

## Delegation

- Fan out independent work; do not serialize without reason.
- You cannot see or message grandchildren.
- Do not micromanage internals while a child is InProgress.

## Progress vs done

- `RespondToParent(Intermediate)` — status/questions; assignment stays open; does not forcibly end the turn.
- `RespondToParent(Final)` — complete answer only; blocked while any direct child is InProgress; ends the turn.
- Ending a turn with InProgress children (no Final yet) is correct and expected.

## Dispose

Kills the child **and its subtree**. Prefer Final → Idle → dispose (or reuse via `MessageSubagent`). Dispose `InProgress` only as an intentional abort.
