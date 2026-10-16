# 00 · Door / window dims to walls and grids (dim cửa tới tường, trục)

**Skill** `drafting-plan-dims` · **Standard** `drafting-plan-dims.md` §1 PD1–PD6 · **Tools** `query_elements_with_filter`, `view_elem_boxes`, `list_view_dims`, `annot_owners`, `dims_at_positions`, `dims_edit`, `dims_text_move`, `annotation_overlaps`, `mark_types`, `ui_view_focus`, `export_sheet_images`
**Applies to** every floor plan view of the job, except OVERALL / key plans and the areas the profile keeps to grid dims only. `dims_at_positions` is one view per call: go through the batch view by view without stopping to report.

## Do

1. **Survey the openings** (per view):
   ```
   query_elements_with_filter {category:"Doors",   viewId, maxCount:500}
   query_elements_with_filter {category:"Windows", viewId, maxCount:500}
   view_elem_boxes {viewId, ids:[door + window ids]}
   ```
   Keep the ones the view draws (`ViewBox` inside the crop, not under a filled / masking region, host only). Group them **by host wall line**: same wall, between the same two grids → one chain (PD4).
2. **What is there already**: `list_view_dims {viewId}` (and `view_elem_boxes {allAnnotations:true}` for lines and texts). A chain that already references both edges of a door, runs the right way (PD2/PD3) and sits by the wall (PD6) → that door is done. A chain on the wall opening edges → not counted (report it). A chain that only lacks the door edges (PD4): `annot_owners {viewId, ids}` first. Own chain → draw the edges as a short chain on the same line (step 4), then `dims_edit` merge `[existing, new]` (the result stays on the first dim's line, removed dims logged). Someone else's → draw a new chain, report it.
3. **Build each chain** in the view frame (Right / Up mm): along the wall, in order:
   - the host grid on each side within ~12 m (`grid → … → grid`, PD2);
   - both **finish faces** of every crossing wall the chain passes (`{mm, id:<wall>, src:"3d"}`, PD3); no grid on a side → end at the last wall finish face there;
   - both edges of every opening (`{mm, id:<door/window>}`: `ViewBox` edges along the wall); roll-up doors: nominal `LEFT` / `RIGHT` (profile family);
   - line position (PD6): exterior wall → outside, ~7 paper mm off the wall face, inside the grid-dim band; interior wall → the side without the door swing.
4. Preview, read the values, then apply:
   ```
   dims_at_positions {viewId, mode:"preview", typeName:<check dim type>, dims:[{name, measure:"right"|"up", lineMm, positions:[...]}]}
   dims_at_positions {viewId, mode:"apply", ..., logPath:"<review>/opening-dims-<viewId>.json"}
   ```
   - A position with no reference → the result lists the nearest ones; take the door's own edge, never the wall opening edge (PD5).
   - Each opening segment = the door / window width (type `Width`; roll-up = nominal). Not equal → wrong reference: fix before apply.
5. **Check it is drawn**: `view_elem_boxes {viewId, ids:<new dims>}`; `Box: null` → delete it (`dims_at_positions` `undo` or `dims_edit`) and change the reference.
6. **Tidy**: `annotation_overlaps {viewId}`; short segments whose texts overlap → `dims_text_move`. Never "Replace with text" (R9).
7. Mark types (`drafting-session`), then export the views and check by image (`drafting-visual-check`).

## Done when

- Every opening the view draws is in a chain that gives its width and its distance to a grid or a wall finish face (PD1–PD3); each opening missing one is listed with its reason (→ Việc tồn).
- Every new dim is drawn (`Box` ≠ null), references only the opening, wall finish faces and host grids (PD5), and sits inside the grid-dim band (PD6).
- `annotation_overlaps`: no new dim over a text, a tag or another dim.

## Skip only if

The view draws no door or window, it is an OVERALL / key plan, or the profile keeps the whole view to grid dims only. Say which.

## Never

- Dim a hidden or linked opening; reference a wall opening edge, a detail line or a link (PD5).
- Push a dim far from its wall to find room: list it for the user.
- Edit or delete someone else's dims; edit a door, wall or grid (R1).
- Dim the door height on a plan.
