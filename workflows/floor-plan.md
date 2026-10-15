# Workflow: floor plans

Floor plan views of the building (stair core plans: use `stair-core.md`).

| # | Step (skill) | Applies to | Done when | Skip only if |
|---|---|---|---|---|
| 1 | `drafting-start` | once | batches formed and shown, views open, snapshot of every batch written | — |
| 2 | `drafting-views-sheets` | each batch | crop set (or Scope Box lock reported as manual), 2D grid ends aligned, no overlapping bubbles, View Template as the profile says | the user says crops/templates are final |
| 3 | `drafting-grid-dims` | each batch | G1–G4: each grid group has one chain + one overall, in the band outside the crop, none clipped | the view shows no grids |
| 4 | `drafting-tags-titles` (room tags) | each batch | `room_tags_outside` = 0; every `?` tag from `room_tags_broken` reported as model-side | the view shows no rooms |
| 5 | `drafting-tags-titles` (viewport titles) | sheets of the batch | `viewport_title_audit` clean on every sheet holding a batch view | the views are not on a sheet yet |
| 6 | `drafting-visual-check` | each batch | checker finds no fault, or each fault is under Cần xem | — |
| 7 | `drafting-end` | once | last mark applied, log written, panes back, report with the checklist | — |

Why this order: crop and grid ends decide where grid dims go (2→3); titles go after every dim and crop change (5).
