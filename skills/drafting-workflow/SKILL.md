---
name: drafting-workflow
description: "Run a whole drafting job as a fixed workflow: skills in a set order, each step with a done-when gate, progress kept in a checklist file so nothing is skipped and work resumes after a break. Workflows: facade elevations/sections, floor plans, stair core, comment round. Use for quy trình, làm trọn bộ, làm hết mặt đứng, triển khai mặt bằng, làm lõi thang, chạy quy trình, tiếp tục quy trình, workflow."
---

# Drafting workflow (quy trình)

A **workflow** is one file in `~/.claude/drafting-domain/workflows/`: the skills of one job, in order, each with the views it applies to, a **done when** gate and a **skip only if** condition. This skill runs it. The steps' skills do the work.

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

## Checklist file (the source of truth)

Create it before step 1, in the round folder `review/<date>_<code>/` if there is one, otherwise `log/`: `workflow-<YYYY-MM-DD>-<workflow>.md`.

```
# <workflow> — <date>
Batches: B1 = <view names> (<ViewType>), B2 = …
| # | Step (skill) | B1 | B2 |
|---|---|---|---|
| 1 | drafting-start | x | x |
| 2 | drafting-views-sheets |  |  |
Marks: x done (gate met) · - skipped (reason below) · ! not done (reason below) · n/a step does not apply to this batch
Notes:
- B2 step 4 skipped: survey shows 0 visible openings (opening_dims_each).
```

- Fill the batches from `drafting-start` (up to 10 views of one ViewType + topic).
- Tick a cell **right after** its gate is met, not at the end of the turn.

## Run

1. **Batch by batch.** For each batch, run the steps that apply to its views, in the workflow's order. Steps marked *once* (start, end) run once for the whole job.
2. **One step at a time.** Load that step's skill (and the one standard it names) only when the step starts; follow the skill as written.
3. **Gate.** A step is done for a batch only when its **done when** holds for every view of the batch. Not met → fix and recheck; still not met → mark `!` with the reason, it goes under Việc tồn. Never start a later step that depends on it as if it were done.
4. **No silent skip.** Skip a step only when its **skip only if** condition is proven by a tool result of this turn, or the user said so; write the reason in Notes. Otherwise do it, or ask.
5. **Order is fixed.** The order exists because later steps depend on earlier ones (crop before grid dims, dims before tags, titles last). A step that changes something an earlier step checked (crop, grid ends, dims) → run that earlier step's check again for the batch.
6. One-line progress to the user after each batch: `B1: 5/7 steps, next: drafting-opening-tags`.
7. "dừng" → stop at once; the checklist shows where.

## Resume ("tiếp tục")

Find the latest `workflow-*.md` in the round folder or `log/`, re-anchor (`hello_revit`), and continue at the first empty cell. Never redo a step marked `x` unless a later change broke its gate.

## Finish

The workflow's last steps (`drafting-visual-check`, `drafting-end`) close it. Add the final checklist table to the report: every cell `x`, `-`, `!` or `n/a`, no empty cell; every `-` and `!` listed under Việc tồn with its reason.
