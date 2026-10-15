# Workflow: facade elevations and building sections

Elevation and section views of the building (not stair views: use `stair-core.md`). Elevation and section are different ViewTypes → separate batches, same steps.

| # | Step (skill) | Applies to | Done when | Skip only if |
|---|---|---|---|---|
| 1 | `drafting-start` | once | batches formed and shown, views open, snapshot of every batch written | — |
| 2 | `drafting-views-sheets` | each batch | crop set (or Scope Box lock reported as manual), 2D grid ends aligned, no overlapping bubbles, View Template as the profile says | the user says crops/templates are final |
| 3 | `drafting-grid-dims` | each batch | G1–G4: each grid group has one chain + one overall, in the band outside the crop, none clipped | the view shows no grids |
| 4 | `drafting-dims` (level dims) | each batch | every level shown has its chain + overall level dim, in the check type | — |
| 5 | `drafting-opening-dims` | each batch | Q1–Q5: every visible opening has its vertical + horizontal chain; `annotation_overlaps` = 0 | the survey shows 0 visible openings |
| 6 | `drafting-opening-tags` | each batch | T1–T8: exactly one tag per visible opening, no duplicates, none over dims | the survey shows 0 visible openings |
| 7 | `drafting-tags-titles` (viewport titles) | sheets of the batch | `viewport_title_audit` clean on every sheet holding a batch view | the views are not on a sheet yet |
| 8 | `drafting-visual-check` | each batch | checker finds no fault, or each fault is under Cần xem | — |
| 9 | `drafting-end` | once | last mark applied, log written, panes back, report with the checklist | — |

Why this order: crop and grid ends decide where grid dims go (2→3); level dims and opening dims come before tags, which must avoid them (4, 5→6); titles sit under the lowest content, so they go after every dim and crop change (7).
