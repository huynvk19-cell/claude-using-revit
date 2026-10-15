# Workflow: facade elevations and building sections

Elevation and section views of the building (not stair views: use `stair-core.md`). Two parts with the same steps: **A elevations**, **B sections** (different ViewTypes). Each step runs on every view of a part before the next step; elevation / section commands one view per call.

Cards: `~/.claude/skills/<skill>/steps/<card>`; each holds what to do, **done when** and **skip only if**. A step without a card (one-step skill) has its gate here.

| # | Step | Skill → card |
|---|---|---|
| 0 | Start: gather every view, sort into A / B, open, snapshot (once) | `drafting-start` |
| 1 | Identify the views | `drafting-views-sheets` → `00-identify` |
| 2 | Viewport type | `drafting-views-sheets` → `01-viewport-type` |
| 3 | Crop | `drafting-views-sheets` → `02-crop` |
| 4 | Grid ends 2D | `drafting-views-sheets` → `03-grid-ends` |
| 5 | View Template, hiding | `drafting-views-sheets` → `04-template` |
| 6 | Grid dims: audit (+ ask the yeses) | `drafting-grid-dims` → `00-audit` |
| 7 | Grid dims: fix | `drafting-grid-dims` → `01-fix-view` |
| 8 | Grid dims: after grid ends extended | `drafting-grid-dims` → `02-extended-ends` |
| 9 | Grid dims: recheck | `drafting-grid-dims` → `03-recheck` |
| 10 | Level dims — done when: every level shown has its chain + overall in the check type · skip only if: the view already has them | `drafting-dims` (`level_dims_add`) |
| 11 | Opening dims: audit | `drafting-opening-dims` → `00-audit` |
| 12 | Opening dims: each opening | `drafting-opening-dims` → `01-dim-each-opening` |
| 13 | Opening dims: tidy | `drafting-opening-dims` → `02-tidy` |
| 14 | Opening dims: recheck | `drafting-opening-dims` → `03-recheck` |
| 15 | Opening tags: audit | `drafting-opening-tags` → `00-audit` |
| 16 | Opening tags: place | `drafting-opening-tags` → `01-place-tags` |
| 17 | Opening tags: duplicates, orphans | `drafting-opening-tags` → `02-duplicates-orphans` |
| 18 | Opening tags: align rows | `drafting-opening-tags` → `03-align-rows` |
| 19 | Opening tags: roll-up doors | `drafting-opening-tags` → `04-rollup-doors` |
| 20 | Opening tags: recheck | `drafting-opening-tags` → `05-recheck` |
| 21 | Viewport titles (sheets of the job) | `drafting-tags-titles` → `01-viewport-titles` |
| 22 | Drawing list, print set | `drafting-views-sheets` → `07-drawing-list` |
| 23 | Visual check of every view | `drafting-visual-check` |
| 24 | End: last mark, log, panes back, report with the checklist (once) | `drafting-end` |

Why this order: crop and grid ends decide where grid dims go (3–4 → 6–9); level dims and opening dims come before tags, which must avoid them (10–14 → 15–20); titles sit under the lowest content, so they go after every dim and crop change (21).
