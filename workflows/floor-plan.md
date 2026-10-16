# Workflow: floor plans

Floor plan views of the building (stair core plans: use `stair-core.md`; ceiling plans: use `ceiling-plan.md`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): every floor plan, or every floor plan on the named sheets |
| 1 | `drafting-views-sheets` | 00–04 | every view |
| 2 | `drafting-grid-dims` | all | every view |
| 3 | `drafting-plan-dims` | all | every view |
| 4 | `drafting-tags-titles` | 00, 01 | every view (00), the sheets of the job (01) |
| 5 | `drafting-views-sheets` | 07 | the sheets of the job |
| 6 | `drafting-visual-check` | — | every view |
| 7 | `drafting-end` | — | once |

Why this order: crop and grid ends decide where grid dims go (1 → 2); inside `drafting-plan-dims` (3) the door / window dims come first (card 00), then the door / window tags that keep clear of them (01), then the room tags that keep clear of every dim and tag (02), all after the grid dims; the room tag check and titles go after every tag, dim and crop change (4); the drawing list last (5).
