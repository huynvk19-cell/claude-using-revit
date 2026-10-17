# 03 · Grid bubbles not covered (bubble trục không bị che)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` G5 · **Tools** `align_grid_ends`, `grid_dims_band`, `view_elem_boxes`, `move_element`, `export_sheet_images`
**Applies to** every view of the job; one view per call.

## Do

1. **Find the covered bubbles** on the exported view and sheet (`drafting-visual-check`), and from the grid spacing: two parallel grids closer than the bubble diameter + 2 paper mm overlap at their bubbles.
   Covered = a bubble on another bubble, or under a dim, tag, text, view marker (section / elevation / callout head), filled / masking region or model linework, another viewport, the title block or outside the sheet border; or a bubble left inside the crop while the rest of its group sits on the bubble row.
2. **Fix by the 2D ends only** (`align_grid_ends`, never 3D extents):
   - **Bubble left inside the crop / on the drawing** (a whole side, or a short grid) → move its 2D end out to its group's bubble row outside the crop (`side:"low"|"high"` keeps the other end):
     `align_grid_ends {mode:"apply", viewId, only:[names], targets:{<group>:{low, high}}, logPath}` with `low` / `high` = the row's current values (read them first with `mode:"preview"`).
   - **Two bubbles overlapping** (close or coincident grids) → stagger **one** of them **outward** (away from the drawing, so the grid dims between crop and bubbles stay put): the same command with `targets` = row value ∓ the stagger on that side. Stagger ≥ bubble diameter + 2 paper mm, the diameter read from the grid's `view_elem_boxes` height (a `6.5mm Bubble` box is 11 mm on paper → **13 mm**, 2600 mm at 1:200); less only when the grids are far enough apart that the bubbles clear (check the image). Move the one drawn behind; same grid on both ends when both ends overlap.
   - **Bubble under a marker, tag, text or another viewport** → stagger or shorten that one 2D end to the nearest clear spot along its grid; nothing clear → Việc tồn.
   - Grid names may carry invisible characters (`‭C`): copy the names exactly from the preview output.
   - `grid_bubble_elbow` (bubble leader) often fails with "leader not valid": use the stagger.
3. **Annotation crop**: run `grid_dims_band {mode:"preview"}` then `apply` (same arguments as card 01, `logPath`): it grows the annotation crop to the moved bubbles (G4) and checks the dims stay `OK`.
4. **Siblings**: dependent views of the same parent may keep their own 2D ends; check that a sibling on another sheet did not change (`align_grid_ends {mode:"preview", viewId:<sibling>, only:[names]}`).
5. Export the view and the sheet again: every bubble clear, none on another viewport or past the border (a staggered bubble on the border → move the viewport toward the free side, `move_element` on the viewport, paper mm); re-place the title if the bubble row moved (card 02).

## Done when

- On the image every bubble of the view reads on its own: no bubble on another bubble, a dim, a tag, a text, a marker, hatch or model lines, another viewport, the title block or outside the border.
- `grid_dims_band` after: every group `OK`.

## Skip only if

The image shows every bubble clear.

## Never

- Edit 3D grid extents; move a bubble inward into the grid-dim band.
- Rename or delete grids (duplicate names → report for the user).
