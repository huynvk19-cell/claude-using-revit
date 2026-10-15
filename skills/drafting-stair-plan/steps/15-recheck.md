# 15 · Recheck (kiểm lại)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` ĐỦ – ĐÚNG – ĐẸP · **Tools** `stair_plan_audit`, `export_sheet_images`, `view_elem_boxes`
**Applies to** every plan view of the job, one view per call.

## Do

1. `stair_plan_audit` again.
2. Export the **view** image (`export_sheet_images {sheetNumbers:[], viewIds:[id]}`) and hand it to `drafting-visual-check`: 3 runs, 3 run tags outside the walls with leaders, a spot on every seen landing, continuous numbers, a V-shaped arrow at the top end of each run, no UP/DOWN, dim lines aligned and in order, short texts apart, no dim line through a CLEAR text or a tag, the X on the open gap. Compare with the views already finished on the same sheet.
3. Report per `drafting-session`: exact view names; table per view of what was added / fixed; **Cần xem** (V1 ≠ V3, widths measured to a run edge, tags moved by hand); **Việc tồn** (model mismatches, link stairs/walls, dims still in the check type).

## Done when

- No `missing`, no `Issues` except those the user accepted.
- Every item of the standard's ĐỦ list on the view (SA1–SA4, SB1–SB6, C, SD, X).

## Skip only if

Never.
