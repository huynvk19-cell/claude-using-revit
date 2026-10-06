---
name: drafting-grid-dims
description: "Grid dimensions on architectural drawings via Revit MCP, rules G1–G4: parallel grids form a group; each group gets exactly ONE grid-to-grid chain and ONE overall dim per drawing, on one side, in the band OUTSIDE the crop and INSIDE the grid bubbles; the annotation crop is pulled out to the bubbles so no grid dim is clipped. Audit, fix one view at a time, verify. Use for dim trục, dim cách trục, dim tổng trục, thiếu dim trục, dim trục trùng, dim trục bị mất, kéo crop annotate, annotation crop, grid dimensions."
---

# Grid dims (G1–G4)

Standard: `~/.claude/drafting-domain/drafting-grid-dims.md` (Vietnamese). Rules G1–G4 decide every case. Read it first.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the check dim type → `dimType`;
- the grid-dim sides → `preferSides`, e.g. `["bottom","left"]`;
- the views without grid dims → `excludeNameContains`.

The tool has no project defaults.

## 1. Audit (read-only)

```
grid_dims_band {mode:"audit", sheetPrefix:<profile prefix>, dimType, preferSides, excludeNameContains, onlyProblems:true}
```

- Plans can be audited many at once.
- Elevations and sections: **one view per call** (`viewIds:[id]`).

Each group comes back with:
- `Side`: the side chosen and why;
- `BandMm`: the room available vs the room needed;
- `Chain` / `Overall`: the dims kept and where each one sits (`in band`, `inside crop`, `beyond bubble`, `on the other side`);
- `Issues`.

Each view also comes back with `AnnoCrop`: `BubbleEndsOutside` and `Need` (how much each side must grow).

## 2. Sort the findings and show the user

Show one table: Project Browser view name → group → issues. Then split the work into three kinds:

| Kind | Examples | Action |
|---|---|---|
| Automatic | missing chain/overall, kept dim outside the band, annotation crop too small | `apply` with defaults |
| Needs a yes | extra dims (`deleteExtra`); band too narrow, i.e. grid ends inside the crop (`extendGrids`, give the mm per side) | ask, then apply with the flag |
| Manual | crop must shrink but a Scope Box locks it; curved grids; no room on any side | list under "Việc tồn" |

Never set `deleteExtra` or `extendGrids` without the user's yes **in this turn**.

## 3. Fix ONE view per call

```
grid_dims_band {mode:"preview", viewId, dimType, preferSides, [deleteExtra], [extendGrids]}
grid_dims_band {mode:"apply",   viewId, ..., logPath:"<review>/grid-band-<viewId>.json"}
```

1. Read `Done` from the preview: created, moved, deleted/hidden, grid ends extended, annotation crop growth.
2. Apply with the same arguments.
3. Check `After`: every group should be `OK`.

**Dependent views:**
- The dims belong to the parent. A moved dim reported "also shown in <view>" changed that view too → audit and fix that sibling next.
- New dims that show up in a sibling are hidden there automatically (`hidden … in …`).

**Batch runs:**
- Before each view, open it for the user: `ui_view_focus {action:"open", viewId}`.
- Pass `compact:true` to get a short result.

**After grid ends were extended (`ExtendedSides`):**
- Move the titles below the new bubble row: `viewport_titles_place {mode:"apply", sheetNumbers, onlyViewports:[vp], ignoreCrop:true, logPath}`.
- Export the sheet and look. Bubbles or dims that land on another viewport, or outside the sheet border:
  1. `undo` that view;
  2. redo it with `forceSides` (e.g. `{horizontal:"right"}`) on a side that still has room;
  3. if no side works, list it under "Việc tồn".
- Dependents with different crops that share one parent chain: keep the chain for one view; in the other, pass `extraIds:[chainId]` (hidden there) so it gets its own chain.

Views whose template hides Dimensions are skipped by the tool (`DimensionsHidden`). Key plans go in the profile exclusions.

## 4. Verify

1. Re-run the audit on the fixed views until they are `OK`.
2. Run `annotation_overlaps` per view.
3. Export the sheet and look at the band and the bubbles (`drafting-visual-check`).
4. If the annotation crop grew, check the viewport against neighbours and the title block, then re-place the titles (`drafting-tags-titles`).

## 5. Report

Follow `drafting-session`:
- exact sheet and view names;
- a table per view: groups OK / fixed / left;
- annotation crop growth in paper mm;
- grid ends extended.

End with:
- **Cần xem**: tight bands, sides without bubbles, dims shared between dependents;
- **Việc tồn**: manual crops, scope boxes, dims still in the check type.

Undo one view: `grid_dims_band {mode:"undo", logPath}`.

## Never

- Dims on both sides of a group.
- More than one chain or overall per group per drawing.
- A grid dim inside the crop or beyond the bubbles.
- Shrinking an annotation crop. Turning on an annotation crop the user did not ask for.
- Referencing link grids.
- Editing 3D grid extents.
- Several elevations or sections in one call.
