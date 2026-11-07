# 04 · Align plan series and elevation rows (căn chuỗi mặt bằng, căn hàng)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP5, VP6; `drafting-sheet-layout.md` LO4, LO5 · **Tools** `sheet_views_ids`, `export_sheet_images` · **Planned** `viewport_place`
**Applies to** the sheets of the job that hold a plan series, or several elevations / sections.

## Do

1. **Plan series**: for each sheet of the series, the reference grid intersection must sit at `P` within 1 mm on paper.
   - Check: the sheet image (`export_sheet_images`) or the viewport position from the readback.
   - Off → move the viewport by the difference.
2. **Elevations / sections on one sheet**:
   - ±0.000 on one line (side by side);
   - grids of the same name in one column (one above the other).
3. Moving:
   - with `viewport_place` (`move:true`): `preview` → `apply` with `logPath`;
   - without it: give the user the move in mm per viewport (right / up), then read back.
4. Report the offsets: before / after, per sheet.

## Done when

- Every plan series sits at its `P` within 1 mm on every sheet; every elevation / section row shares its level line or grid column.

## Skip only if

The job has no plan series and no sheet with several elevations / sections.

## Never

- Move a viewport by its box centre to align a series (VP5).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
