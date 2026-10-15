---
name: drafting-workflow
description: "Run a whole drafting job as a fixed workflow: skills in a set order, each step with a done-when gate, each step done on all views before the next, progress kept in a checklist file so nothing is skipped and work resumes after a break. Workflows: facade elevations/sections, floor plans, stair core, comment round. Use for quy trình, làm trọn bộ, làm hết mặt đứng, triển khai mặt bằng, làm lõi thang, chạy quy trình, tiếp tục quy trình, workflow."
---

# Drafting workflow (quy trình)

A **workflow** is one file in `~/.claude/drafting-domain/workflows/`: the **skills** of one job in order, which of their cards to run (default all), the views each applies to, and why this order. It holds no step content: the steps are the skills' **step cards** (`skills/<skill>/steps/`), each with its own **done when** and **skip only if**. This skill runs it: **one card on all views, then the next card**.

**Tools**: none of its own. Each step loads its skill's **Tools** line when the step starts.

Load `drafting-session` first if it is not loaded yet (hard rules, batches, mark types).

## Pick the workflow

| Job | Workflow file |
|---|---|
| Facade elevations, building sections (not stair views) | `workflows/elevation-section.md` |
| Floor plans | `workflows/floor-plan.md` |
| Stair core: plans + sections along + sections across | `workflows/stair-core.md` |
| A round of reviewer comments (PDF, TASKS.md) | `workflows/review-round.md` |

- Read **only** the chosen file. Unclear which one, or the job fits none → ask; do not mix two workflows in one checklist.
- A single-topic task ("dim trục view này") needs no workflow: use the topic's skill from the `drafting-session` table.

## Gather the views once

At the start (`drafting-start`), collect **every** view / sheet the job covers and sort them into the workflow's parts (by view kind: plans, elevations, sections along, sections across…). This list is fixed for the whole job; a view found later is added to its part and catches up on the steps already done.

## Expand the rows into steps

For each row of the workflow, in order: open the skill's `SKILL.md` and take its **Step cards** table (only the cards the row names). Each card is one step. A skill without cards (`drafting-start`, `drafting-end`, `drafting-visual-check`, `drafting-dims`…) is one step; its gate is the skill's own check / **Done when**.

## Checklist file (the source of truth)

Create it before the first step, in the round folder `review/<date>_<code>/` if there is one, otherwise `log/`: `workflow-<YYYY-MM-DD>-<workflow>.md`. One table per part: rows = the expanded steps (`<row>.<card>`, e.g. `A.01 SB1 run tags`), columns = the part's views.

```
# stair-core — <date>
Views: A: 1 = <plan name>, 2 = …   B: 1 = <section name> …
## A. Plans
| # | Item | 1 | 2 | 3 |
|---|---|---|---|---|
| A.00 | survey | x | x | x |
| A.01 | SB1 run tags | x | x | x |
| A.02 | SA1 clear width |  |  |  |
Marks: x done (gate met) · - skipped (reason below) · ! not done (reason below) · n/a
Notes:
- A.12 view 2 skipped: the view draws no door (stair_plan_audit Tags).
```

Tick a cell **right after** its gate is met for that view, not at the end of the turn.

## Run — step by step over all views

1. **Step-major.** Take the first card and do it on **every view of its part**; only then the next card. Never finish one view through all steps while the others wait.
2. **Batches inside a step.** More than 10 views of one kind → the step runs batch after batch (up to 10 views each, `drafting-session`) before the next step. Elevation / section commands stay one view per call.
3. **Load as you go.** Load a row's skill (and the one standard it names) when the row starts; open only the card of the step at hand, and load the commands on its **Tools** line (one ToolSearch).
4. **Gate.** A cell is `x` only when the card's **done when** holds on that view. Not met → fix and recheck; still not met → `!` with the reason (goes under Việc tồn). The next step still runs on the other views.
5. **No silent skip.** Skip a cell only when the card's **skip only if** is proven by a tool result of this turn, or the user said so; write the reason in Notes. Otherwise do it, or ask.
6. **Order is fixed**: the workflow's rows, then each skill's card list. A step that moves something an earlier step checked → recheck that earlier step on the view.
7. Progress line to the user after each step: `A.02 SA1 clear width: 5/6 views, 1 ! (view 4: rail edge not drawable)`.
8. "dừng" → stop at once; the checklist shows where.

## Resume ("tiếp tục")

Find the latest `workflow-*.md` in the round folder or `log/`, re-anchor (`hello_revit`), and continue at the first empty cell. Never redo a cell marked `x` unless a later change broke its gate.

## Finish

The workflow's last steps (`drafting-visual-check`, `drafting-end`) close it. Add the final checklist table to the report: every cell `x`, `-`, `!` or `n/a`, no empty cell; every `-` and `!` listed under Việc tồn with its reason.
