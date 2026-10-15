# Workflow: stair core

All views of one stair core, gathered once at the start and sorted by view kind.

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather every plan and section of the core, sort into A / B / C |
| A | `drafting-stair-plan` | all | the plans |
| B | `drafting-stair-section-parallel` | all | the sections along the flights |
| C | `drafting-stair-section-cross` | all | the sections across the flights |
| D | `drafting-visual-check` | — | every view of the job |
| E | `drafting-end` | — | once |

Why this order: plans before sections, because the sections repeat the plans' run names, riser counts, numbers and levels. Stair-view titles stay as they are unless a comment asks.
