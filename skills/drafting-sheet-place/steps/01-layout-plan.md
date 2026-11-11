# 01 · Layout plan per sheet in paper mm (bảng bố trí)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP3–VP6, §2; `drafting-sheet-layout.md` LO1–LO10, Z2–Z3 · **Tools** none (works on the register)
**Applies to** the sheets of the job.

## Do

1. **Drawing area** of each sheet (LO1): title block size minus margins, title strip and notes column (profile mm). Origin = the sheet's lower-left corner.
2. **Fixed items first** (VP4, LO8): the notes column, key plan, north arrow, legend, schedule spots.
3. **Views** in reading order (LO3), with gaps ≥ the profile gap (LO6). Rules by kind:
   - single views: the centre of their cell;
   - **plan series**: one paper point `P` for the reference grid intersection, the same `P` on every sheet of the series (VP5). Choose `P` so that the biggest view of the series fits;
   - elevations / sections on one sheet: ±0.000 on one line, or grids in one column (VP6);
   - detail sheets: an even grid of cells (LO9).
4. Check the plan on paper:
   - every box inside the drawing area;
   - gaps ≥ the profile gap;
   - density 40–85 % (LO10).

   A sheet that fails → regroup it (`drafting-sheet-create` card 01) or ask.
5. Write `Vị trí (mm)` per row (centre, or `P` + the two reference grids) into the register:

   | Sheet | View | Kind of point | X, Y (mm) | Box W × H (mm) | Rule |
   |---|---|---|---|---|---|

## Done when

- Every row of the job has a `Vị trí`; every box is inside the drawing area with the gap kept.
- Each plan series shares one `P`; rows of elevations / sections share their level line.

## Skip only if

Every row is already `placed` at a position that card 05 found clean.

## Never

- Rotate a plan against the set's orientation to make it fit.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
