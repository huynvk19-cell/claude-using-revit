# Workflow: facade elevations and building sections

Elevation and section views of the building (not stair views: use `stair-core.md`). Two parts: **A elevations**, **B sections** (different ViewTypes), same rows.

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): elevations → A, sections → B |
| 1 | `drafting-views-sheets` | 00–04 | every view |
| 2 | `drafting-grid-dims` | all | every view |
| 3 | `drafting-dims` | level dims | every view |
| 4 | `drafting-opening-dims` | all | every view |
| 5 | `drafting-opening-tags` | all | every view |
| 6 | `drafting-tags-titles` | 01 | the sheets of the job |
| 7 | `drafting-views-sheets` | 07 | the sheets of the job |
| 8 | `drafting-visual-check` | — | every view |
| 9 | `drafting-end` | — | once |

Why this order: crop and grid ends decide where grid dims go (1 → 2); level and opening dims come before tags, which must avoid them (3, 4 → 5); titles sit under the lowest content, so they go after every dim and crop change (6); the drawing list last (7).
