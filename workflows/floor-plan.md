# Workflow: floor plans

Floor plan views of the building (stair core plans: use `stair-core.md`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once |
| 1 | `drafting-views-sheets` | 00–04 | every view |
| 2 | `drafting-grid-dims` | all | every view |
| 3 | `drafting-tags-titles` | 00, 01 | every view (00), the sheets of the job (01) |
| 4 | `drafting-views-sheets` | 07 | the sheets of the job |
| 5 | `drafting-visual-check` | — | every view |
| 6 | `drafting-end` | — | once |

Why this order: crop and grid ends decide where grid dims go (1 → 2); titles go after every dim and crop change (3); the drawing list last (4).
