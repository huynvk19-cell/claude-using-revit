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
2. **Preview** to list the visible openings and the dims they already have:
   ```
   elevation_opening_dims {viewId, mode:"preview", strictVisibility:true,
                           verticalMode:"all", gridNearDist:12000,
                           nominalFamilies:["ROLL UP"], moveExisting:false, logPath}
   ```
3. **Check the plan against Q2.** The tool picks the nearest level below the opening, not its host level. Compare with each instance's Level parameter.
   - Where they differ (mezzanines, doors on landings), drop that chain and use `opening_vdims {items:[{openingId, xMm, levelId:<host level>}]}`.
   - If needed, extend the tool with a `hostLevel` option.
4. **Apply.**
5. **Run `dedupe_new_vdims {fromLog}`.** It removes new chains that duplicate an existing correct dim.
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
