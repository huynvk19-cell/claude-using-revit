# 05 · Verify, images, update the register (kiểm, cập nhật sổ)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP10 · **Tools** `sheet_views_ids`, `export_sheet_images`
**Applies to** the sheets of the job.

## Do

1. Read back once: `sheet_views_ids` per prefix (`includeAll:true`).
2. Export the sheets of the job once: `export_sheet_images {sheetNumbers:[…]}`. These images are reused by `drafting-sheet-layout` card 04 and by `drafting-visual-check`.
3. Check every row against VP10:
   - viewport id on the right sheet;
   - position = plan ±1 mm (grid point for series);
   - nothing outside the drawing area, nothing overlapping (image).
4. Rows that pass → state `placed`. Rows that fail → back to card 03 / 04, with the reason in `Ghi chú`.

## Done when

- Every register row that is not `n/a` is `placed` (or later), with `Viewport id` and `Viewport type`.
- The sheet images of the job exist for the layout check.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
