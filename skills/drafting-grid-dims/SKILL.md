---
name: drafting-grid-dims
description: "Grid dimensions on architectural drawings via Revit MCP, rules G1–G5: one grid-to-grid chain + one overall dim per grid group, in the band outside the crop and inside the bubbles; annotation crop pulled to the bubbles; no covered bubbles (elbows). Use for dim trục, dim cách trục, dim tổng trục, thiếu dim trục, dim trục trùng, dim trục bị mất, kéo crop annotate, annotation crop, bubble trục bị che, bubble chồng nhau, elbow bubble, bubble cùng hàng, grid dimensions."
---

# Grid dims (G1–G5)

**Tools** (load all in one ToolSearch call): `align_grid_ends`, `annotation_overlaps`, `export_sheet_images`, `grid_bubble_elbow`, `grid_dims_band`, `move_element`, `ui_view_focus`, `view_elem_boxes`, `viewport_titles_place`.

Standard: `~/.claude/drafting-domain/drafting-grid-dims.md` (Vietnamese). Rules G1–G5 decide every case. Read it by section (`drafting-session`, Standards by section): the G1–G5 summary first, then the rule the card names.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the check dim type → `dimType`;
- the grid-dim sides → `preferSides`, e.g. `["bottom","left"]`;
- the views without grid dims → `excludeNameContains`.

The tool has no project defaults.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-audit.md` | Audit (khảo sát dim trục) |
| 01 | `steps/01-fix-view.md` | Fix (sửa dim trục, mỗi lần một view) |
| 02 | `steps/02-extended-ends.md` | After grid ends were extended (sau khi kéo đầu trục) |
| 03 | `steps/03-bubbles.md` | Grid bubbles not covered (bubble trục không bị che) |
| 04 | `steps/04-recheck.md` | Recheck (kiểm lại) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Dims on both sides of a group.
- More than one chain or overall per group per drawing.
- A grid dim inside the crop or beyond the bubbles.
- Shrinking an annotation crop. Turning on an annotation crop the user did not ask for.
- Referencing link grids.
- Editing 3D grid extents.
- Staggering a bubble out of its row to clear another bubble (elbow instead, G5).
- Several elevations or sections in one call.
