# 14 · Tidy (dọn chồng lấn)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` ĐẸP · **Tools** `annotation_overlaps`, `dims_text_move`, `tag_leaders_info`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

- `annotation_overlaps {viewId}` → fix every overlap.
- Formula texts off the riser numbers and the witness lines (`dims_text_move`).
- `tag_leaders_info` → no diagonal leader.

## Done when

- `annotation_overlaps` = 0; formula texts clear of the numbers.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
