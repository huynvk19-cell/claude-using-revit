# 03 · Grid ends 2D (đầu trục)

**Skill** `drafting-views-sheets` · **Standard** `drafting-views-sheets.md` § grids · **Tools** `align_grid_ends`, `grid_bubble_elbow`
**Applies to** every view of the job (crop, grid ends, template), or the sheets of the job (viewport type, titles, drawing list).

## Do

- 2D ends only. `align_grid_ends` preview → apply lines the bubbles up.
- Overlapping or covered bubbles → `drafting-grid-dims` card 03 (G5): bubbles of a side stay in one row; only an overlapping pair gets an elbow on one bubble, toward the roomier side (`grid_bubble_elbow`), never a stagger.
- Pull grids out only when asked, and move the grid dims with them: `align_grid_ends {side:"low"|"high", targets}` moves one end only, then `grid_dims_band` apply with a `cropGapMm` that clears what was in the band.

## Done when

- Bubbles in one line per side, none overlapping (overlapping pairs elbowed).

## Skip only if

The view shows no grids.

## Never

- Edit 3D grid extents.
