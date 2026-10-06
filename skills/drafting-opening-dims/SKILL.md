---
name: drafting-opening-dims
description: "Dimension every visible window and door (incl. roll-up doors) on elevation and section views: vertical chain host level → sill → head, horizontal chain grid → edge → edge → grid, placed right next to the opening, referencing the opening itself. Use for dim cửa sổ, dim cửa đi, dim cửa cuốn, dim chiều cao cửa, dim khoảng cách cửa, dim mặt đứng, dim mặt cắt, opening dimensions."
---

# Opening dims (elevations / sections)

Standard: `~/.claude/drafting-domain/drafting-opening-dims.md`. Rules Q1–Q5 decide every case. Read it first.

## The five rules

- **Q1** — every visible opening gets one vertical dim and one horizontal dim.
- **Q2** — vertical chain: host level → sill → head.
- **Q3** — horizontal chain: grid → edge → edge → grid.
- **Q4** — place each dim as close to the opening as possible.
- **Q5** — reference the opening itself.

## Steps — ONE view per call, `timeoutSeconds` set

1. **Re-anchor** the view id. Note the view's levels and grids.
2. **Preview, then apply.** `hostLevel:true` enforces Q2 and `verticalMode:"all"` enforces Q1:
   ```
   elevation_opening_dims {viewId, mode:"preview", strictVisibility:true,
                           verticalMode:"all", hostLevel:true, gridNearDist:12000,
                           nominalFamilies:["ROLL UP"], moveExisting:false, logPath}
   ```
   What `hostLevel` does:
   - Every vertical chain starts at the instance's own Level.
   - A door standing above that level gets host level → bottom → top.
   - An existing vertical dim counts only when it includes the host level.
   - Own check-type dims of the opening that start at another level are deleted (`replaceStale`).
   - If the host level is not shown in the view, the chain is bottom → top and it is reported.

   Other behaviour:
   - Horizontal chains take the nearest grid on each side plus the grids between the openings (Q3).
   - Dims owned by the parent of a dependent view count as existing.
3. **Read the `HostLevel` notes and the "complete existing" values.** Doors far above their host level (platforms, mezzanine doors) read e.g. 2200. List them for the user.
4. **Delete a new H chain that repeats an existing one** with `dims_edit`. This happens when the old chain sits outside the row band.
5. **Run `dedupe_new_vdims {fromLog}`** if the view had older vertical dims of another type.
6. **Fix up roll-up doors.** Check with `rollup_dims_check`. Fix with `rollup_dims_add` (TOP for vertical, LEFT/RIGHT for horizontal).
7. **Long chains.** If a segment is over ~50 m → `dims_split {maxMm:50000}`.
8. **Verify** (`drafting-visual-check`) for each opening:
   - V and H dims present (Q1);
   - values read right: no 50 / 2135 / odd frame sizes (Q2, Q3);
   - dims sit next to it, not above the roof or outside the building (Q4);
   - no dim lands on a wall or slab (Q5).
9. **Report** per view:
   - openings dimensioned;
   - openings skipped as hidden;
   - rows with "no free line" (to place by hand);
   - black dims that reference walls instead of the opening.

## Never

- Dimension a hidden opening.
- Draw a vertical dim across its own opening.
- Push a dim into another storey to find space.
- Reference a link.
