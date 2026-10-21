# 15 · Recheck (kiểm lại)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` ĐỦ – ĐÚNG – ĐẸP · **Tools** `stair_xsection_info`, `export_sheet_images`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).

## Do

1. `stair_xsection_info {viewId, rules:["XA","XB","XC"]}`: the count per rule and only the rows / issues left.
2. Export the **view** image and compare with the standard's sample (`drafting-visual-check`).
3. Report per `drafting-session`: exact view name; table per band (rise dim, tags, numbers); **Cần xem** (numbering vs plan); **Việc tồn**.

## Done when

- XA1 rows `OK`, `ClearHeights` complete on one half, no `Issues` except those the user accepted.
- Every row of the standard's ĐỦ list on the view (XA1–XA6, XB1–XB6, XC).

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
