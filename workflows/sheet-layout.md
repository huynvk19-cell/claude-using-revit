# Workflow: sheet layout (layout bộ hồ sơ, dàn trang)

The architectural sheet set of the building, built in Revit:
1. which drawings the building type needs (the sheet register);
2. the sheets created;
3. the views created;
4. the views put on the sheets;
5. the layout checked.

Drafting inside the views (dims, tags) uses the other workflows (`floor-plan.md`, `elevation-section.md`, `ceiling-plan.md`, `stair-core.md`).

Each row calls a skill; the skill's card list gives the steps and the cards give what to do, **done when** and **skip only if** (`drafting-workflow` expands the rows into the checklist). This file adds only the order, the views and the cards to run.

**Two records, both kept up to date**:
- the workflow checklist (`drafting-workflow`): steps × sheets of the job;
- the **sheet register** (`drafting-sheet-layout.md` §11): one row per drawing, whose state moves `planned → sheet → view → placed → checked`.

A row of this workflow may start only on register rows that the row before brought to its state.

| # | Skill | Cards | Applies to |
|---|---|---|---|
| 0 | `drafting-start` | — | once: gather the views / sheets of the job (`drafting-workflow` step 0): every sheet of the set (the drawing list + every sheet with the profile prefix) and every view placed on them; views not yet on a sheet join at row 1 (card 02) |
| 1 | `drafting-sheet-layout` | 00–03 | once (the whole set): type + stage → register `planned` → gap with the model → scale / paper / zones |
| 2 | `drafting-sheet-create` | all | register rows `planned` → `sheet` (new sheets join the sheets of the job) |
| 3 | `drafting-view-create` | all | register rows `sheet` → `view` (new views join the views of the job) |
| 4 | `drafting-views-sheets` | 00, 02, 03 | every view of the register (identify, crop, grid ends) |
| 5 | `drafting-sheet-place` | all | register rows `view` → `placed`, the sheets of the job |
| 6 | `drafting-tags-titles` | 01 | the sheets of the job |
| 7 | `drafting-sheet-layout` | 04 | the sheets of the job: register rows `placed` → `checked` |
| 8 | `drafting-views-sheets` | 07 | the sheets of the job |
| 9 | `drafting-visual-check` | — | the sheets of the job |
| 10 | `drafting-end` | — | once |

Why this order:
- The building type and the stage decide the register. The gap against the model and the zones decide how many sheets and views there are (1).
- Sheets come before views, so that each new view is made for a known sheet and a known scale (2 → 3).
- Crops and grid ends fix the size of each view on paper (4). They come before the layout plan and the placing (5); a crop changed after placing means re-planning that sheet.
- `drafting-sheet-place` also sets the viewport types.
- Titles change the space a viewport takes, so they come after placing (6). The layout check needs the final titles (7).
- Then come the drawing list / print set (8), and last the check by image (9).
