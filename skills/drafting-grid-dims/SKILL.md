---
name: drafting-grid-dims
description: "Grid dimensions on architectural drawings via Revit MCP, rules G1–G4: parallel grids form a group; each group gets exactly ONE grid-to-grid chain and ONE overall dim per drawing, on one side, in the band OUTSIDE the crop and INSIDE the grid bubbles; the annotation crop is pulled out to the bubbles so no grid dim is clipped. Audit, fix one view at a time, verify. Use for dim trục, dim cách trục, dim tổng trục, thiếu dim trục, dim trục trùng, dim trục bị mất, kéo crop annotate, annotation crop, grid dimensions."
---

# Grid dims (G1–G4)

**Tools** (load all in one ToolSearch call): `annotation_overlaps`, `export_sheet_images`, `grid_dims_band`, `ui_view_focus`, `viewport_titles_place`.

Standard: `~/.claude/drafting-domain/drafting-grid-dims.md` (Vietnamese). Rules G1–G4 decide every case. Read it first.

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
| 03 | `steps/03-recheck.md` | Recheck (kiểm lại) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Dims on both sides of a group.
- More than one chain or overall per group per drawing.
- A grid dim inside the crop or beyond the bubbles.
- Shrinking an annotation crop. Turning on an annotation crop the user did not ask for.
- Referencing link grids.
- Editing 3D grid extents.
- Several elevations or sections in one call.
