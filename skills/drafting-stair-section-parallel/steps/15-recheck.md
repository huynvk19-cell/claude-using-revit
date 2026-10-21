# 15 · Recheck (kiểm lại)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` ĐỦ – ĐÚNG – ĐẸP · **Tools** `stair_section_info`, `export_sheet_images`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

1. `stair_section_info {viewId, rules:["LA","LB","LC"]}`: the count per rule and only the rows / issues left.
2. Export the **view** image and compare with the standard's "Mẫu tham chiếu" (`drafting-visual-check`).
3. Report per `drafting-session`: exact view name; table per flight (tag, rise dim, going dim, numbers); table per cut landing (LA5: 1200, + 900 where P01 is seen); **Cần xem** (unreadable sample values, LA4 CLEAR yes/no); **Việc tồn**.

## Done when

- Every `Dims` row `OK` (LA5 one row per cut landing), no `Issues` except those the user accepted.
- Every item of the standard's ĐỦ list on the view.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
