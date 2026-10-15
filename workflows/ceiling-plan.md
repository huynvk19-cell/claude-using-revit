# Workflow: ceiling plans

Reflected ceiling plan views of the building (floor plans: use `floor-plan.md`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): every ceiling plan, or every ceiling plan on the named sheets |
| 1 | `drafting-views-sheets` | 00–04 | every view |
| 2 | `drafting-grid-dims` | all | every view |
| 3 | `drafting-ceiling-plan` | all | every view |
| 4 | `drafting-tags-titles` | 00, 01 | every view (00), the sheets of the job (01) |
| 5 | `drafting-views-sheets` | 07 | the sheets of the job |
| 6 | `drafting-visual-check` | — | every view |
| 7 | `drafting-end` | — | once |

Why this order: crop and grid ends decide where grid dims go (1 → 2); room tags go after the grid dims so they keep clear of them (3); the room tag check and titles go after every tag, dim and crop change (4); the drawing list last (5).
