# Workflow: floor plans

Floor plan views of the building (stair core plans: use `stair-core.md`). Each step runs on every view before the next step; plans may go many per call where the card says so.

Cards: `~/.claude/skills/<skill>/steps/<card>`; each holds what to do, **done when** and **skip only if**. A step without a card (one-step skill) has its gate here.

| # | Step | Skill → card |
|---|---|---|
| 0 | Start: gather every view, open, snapshot (once) | `drafting-start` |
| 1 | Identify the views | `drafting-views-sheets` → `00-identify` |
| 2 | Viewport type | `drafting-views-sheets` → `01-viewport-type` |
| 3 | Crop | `drafting-views-sheets` → `02-crop` |
| 4 | Grid ends 2D | `drafting-views-sheets` → `03-grid-ends` |
| 5 | View Template, hiding | `drafting-views-sheets` → `04-template` |
| 6 | Grid dims: audit (+ ask the yeses) | `drafting-grid-dims` → `00-audit` |
| 7 | Grid dims: fix | `drafting-grid-dims` → `01-fix-view` |
| 8 | Grid dims: after grid ends extended | `drafting-grid-dims` → `02-extended-ends` |
| 9 | Grid dims: recheck | `drafting-grid-dims` → `03-recheck` |
| 10 | Room tags | `drafting-tags-titles` → `00-room-tags` |
| 11 | Viewport titles (sheets of the job) | `drafting-tags-titles` → `01-viewport-titles` |
| 12 | Drawing list, print set | `drafting-views-sheets` → `07-drawing-list` |
| 13 | Visual check of every view | `drafting-visual-check` |
| 14 | End: last mark, log, panes back, report with the checklist (once) | `drafting-end` |

Why this order: crop and grid ends decide where grid dims go (3–4 → 6–9); titles go after every dim and crop change (11).
