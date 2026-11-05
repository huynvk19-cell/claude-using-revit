# 05 · Layout plan per sheet (bố trí viewport trên từng sheet)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §6 (LO1–LO10), §7 (Z2–Z3) · **Tools** `sheet_views_ids`, `view_crop_info`
**Applies to** the sheets of the job.

## Do

1. **Drawing area** of the sheet (LO1): the title block outline minus the title strip, the notes column and the margins (profile mm). Sheet coordinates in mm from the sheet's lower-left corner.
2. **Box of each view on paper**: the crop + annotation crop at its scale (card 03), plus the room for the title below it (LO7).
3. **Place the boxes**, in this order:
   1. the key plan, the legend / notes column and the north arrow first (LO8);
   2. the main view top-left or centred (LO3);
   3. the others left → right, top → bottom (LO3);
   4. gaps ≥ the profile gap (LO6);
   5. same top or bottom edge along a row (LO5).

   Rules by sheet kind:
   - **Plans of the same kind on different sheets**: one reference grid intersection (e.g. the first grid of each direction) at the **same sheet point** on every sheet of the series (LO4), per zone.
   - **Elevations / sections side by side**: ±0.000 on one line. One above the other: grids of the same name in one column (LO5).
   - **Detail sheets**: an even grid of cells (LO9).
   - **Zone sheets**: matchline sides and the key plan (Z2, Z3).
4. Write the plan per sheet into the target list file:

   | Sheet | View | Centre X, Y (mm) or reference grid point | Box W × H (mm) | Rule |
   |---|---|---|---|---|

5. Check the plan on paper before card 06:
   - no box outside the drawing area;
   - no two boxes closer than the gap;
   - density 40–85 %.

   A sheet that fails → regroup it (back to card 04) or ask.

## Done when

- Every sheet of the job has a layout plan.
- Every box of the plan is inside the drawing area, with gaps ≥ the profile gap.
- Plan series share their reference grid point (LO4); rows share an edge (LO5).

## Skip only if

The sheet is already laid out and card 07 found it clean.

## Never

- Rotate a plan against the set's orientation to make it fit.
- Plan a view on two sheets (LO11).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
