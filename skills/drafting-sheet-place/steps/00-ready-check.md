# 00 · Ready check: rows, crops, scales (kiểm sẵn sàng)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP1, VP2 · **Tools** `sheet_views_ids`, `view_crop_info`
**Applies to** every register row in state `view`.

## Do

1. Gate each row:
   - state `view`, with `Sheet id` and `View id`;
   - the view is on no sheet (`sheet_views_ids` per prefix);
   - `drafting-views-sheets` cards 02 (crop) and 03 (grid ends) are ticked for the view in the checklist.
2. `view_crop_info` per batch of up to 10 views: the crop + annotation crop and the scale. Compute the box on paper (mm) by §2 of the standard and write it in `Ghi chú`.
3. Rows that fail go back:
   - crop or grid ends not done → `drafting-views-sheets`;
   - scale ≠ register → the user;
   - view on another sheet → ask.

## Done when

- Every row is ready (box on paper known), or sent back with its reason.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
