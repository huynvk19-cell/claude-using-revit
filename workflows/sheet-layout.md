# Workflow: sheet layout (layout bộ hồ sơ)

The architectural sheet set of the building: which drawings the building type needs, which sheets hold them, where each view sits. Drafting inside the views (dims, tags) uses the other workflows (`floor-plan.md`, `elevation-section.md`, `ceiling-plan.md`, `stair-core.md`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): every sheet of the set (the drawing list + every sheet with the profile prefix) and every view placed on them; views not yet on a sheet join the list at row 1 (card 02) |
| 1 | `drafting-sheet-layout` | 00–02 | once (the whole set) |
| 2 | `drafting-views-sheets` | 00, 02, 03 | every view of the job (placed + not placed) |
| 3 | `drafting-sheet-layout` | 03 | every view of the job |
| 4 | `drafting-sheet-layout` | 04 | the sheets of the job (new sheets join the list) |
| 5 | `drafting-sheet-layout` | 05, 06 | the sheets of the job |
| 6 | `drafting-views-sheets` | 01 | the sheets of the job |
| 7 | `drafting-tags-titles` | 01 | the sheets of the job |
| 8 | `drafting-sheet-layout` | 07 | the sheets of the job |
| 9 | `drafting-views-sheets` | 07 | the sheets of the job |
| 10 | `drafting-visual-check` | — | the sheets of the job |
| 11 | `drafting-end` | — | once |

Why this order:
- The building type and the stage decide the target set, and the gap against the model decides which views and sheets the job holds (1).
- Crops and grid ends fix each view's size on paper (2), so they come before the scale / fit / zone check (3).
- Zones and scales decide how the drawings group into sheets and what the sheets are numbered (4).
- The sheets must exist before views are planned and placed on them (5).
- Viewport types and titles change the space a viewport takes, so they come after placing (6, 7).
- The layout check needs the final titles (8).
- Then come the drawing list / print set (9), and last the check by image (10).
