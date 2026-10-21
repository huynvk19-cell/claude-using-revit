# 03 · Grid bubbles not covered (bubble trục không bị che)

**Skill** `drafting-grid-dims` · **Standard** `drafting-grid-dims.md` G5 · **Tools** `align_grid_ends`, `grid_bubble_elbow`, `grid_dims_band`, `view_elem_boxes`, `move_element`, `export_sheet_images`
**Applies to** every view of the job; one view per call.

## Do

1. **Find the covered bubbles** on the exported view and sheet (`drafting-visual-check`), and from the grid spacing: `grid_bubble_elbow {viewId, mode:"plan"}` lists per group and side the bubble pairs that overlap (closer than bubble + 0.5 paper mm: `Overlaps`, with the grid to elbow, the side and the offset), the ends off the row (`OffRow`), the elbows already there (`Elbows`, bubble offset from its grid line) and the measured bubble size.
   Covered = a bubble on another bubble, or under a dim, tag, text, view marker (section / elevation / callout head), filled / masking region or model linework, another viewport, the title block or outside the sheet border; or a bubble left inside the crop while the rest of its group sits on the bubble row.
2. **One row per side first** (2D ends only, `align_grid_ends`, never 3D extents): every bubble of a group on one side sits on the same row outside the crop, staggered ends included.
   `align_grid_ends {mode:"apply", viewId, only:[names], side:"low"|"high", targets:{<group>:{low, high}}, logPath}` with `low` / `high` = the row's current values (read them first with `mode:"preview"`); `side` keeps the other end. This covers a whole side left inside the drawing, a short grid with its bubble on the drawing, and an end staggered out of the row earlier.
3. **Overlapping bubbles → elbow, never stagger**: run `grid_bubble_elbow {mode:"plan"}` again on the lined-up row, then `preview` → `apply` with `logPath` (no `items` = the plan's proposals, or pass `items:[{grid, end, toward, offsetMm}]`).
   - Only the pairs that overlap get an elbow; all other bubbles stay plain on the row.
   - **One** bubble of the pair gets the elbow: the one whose outer side (away from the other grid) has more room to its next neighbour; it moves sideways toward that room until its centre is bubble + 2 mm from the other bubble. When the same pair is already elbowed at the grids' other end (often by the user), the plan elbows the same grid on the same side, so both ends match.
   - Shape = Revit's own Add Elbow shape (knee about 6 paper mm inside the end, back on the line at twice that); a short jog across the grid-dim lines is normal, as when the user adds the elbow by hand.
   - `Tight` in the plan = the moved bubble would touch its next neighbour too → check the image; elbow the other grid instead (`toward` the other way) or list it under Việc tồn.
   - `Error: no valid leader shape` → Việc tồn with the grid name; never fall back to a stagger.
   - An elbow already there that still overlaps → the plan re-sets it (`Note: re-set the elbow`); an elbow on the other grid of the pair that does not clear → remove it with the grid's Remove Elbow grip (`Note`) or list it.
   - Grid names may carry invisible characters (`‭C`): the command matches without them; copy the names from the plan output anyway.
4. **Bubble under a marker, tag, text or another viewport** → elbow it sideways to the nearest clear spot (`items` with `toward` + `offsetMm`); nothing clear → Việc tồn.
5. **Annotation crop**: run `grid_dims_band {mode:"preview"}` then `apply` (same arguments as card 01, `logPath`): it grows the annotation crop to the bubbles (G4) and checks the dims stay `OK`.
6. **Siblings**: dependent views of the same parent may keep their own 2D ends and elbows; check that a sibling on another sheet did not change (`align_grid_ends {mode:"preview", viewId:<sibling>, only:[names]}`, `grid_bubble_elbow {mode:"plan", viewId:<sibling>}`).
7. Export the view and the sheet again: every bubble clear, none on another viewport or past the border (a bubble on the border → move the viewport toward the free side, `move_element` on the viewport, paper mm); re-place the title if the bubble row moved (card 02).

## Done when

- `grid_bubble_elbow {mode:"plan"}`: `OffRow` empty, every `Overlaps` entry `already elbowed`.
- On the image every bubble of the view reads on its own and the bubbles of a side read as one row: no bubble on another bubble, a dim, a tag, a text, a marker, hatch or model lines, another viewport, the title block or outside the border.
- `grid_dims_band` after: every group `OK`.

## Skip only if

The image shows every bubble clear and on its row.

## Never

- Stagger a bubble out of its row (along the grid) to clear another bubble; elbow both bubbles of a pair.
- Edit 3D grid extents; move a bubble inward into the grid-dim band.
- Rename or delete grids (duplicate names → report for the user).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
