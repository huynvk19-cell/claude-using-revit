# 14 · Tidy (dọn chồng lấn)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` ĐẸP · **Tools** `annotation_overlaps`, `finish_marks_snap`, `tag_leaders_info`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).

## Do

- `annotation_overlaps {viewId}` → fix every overlap.
- `finish_marks_snap {mode:"survey"}` → every dot on its face.
- `tag_leaders_info` → no diagonal leader.

## Done when

- Overlaps = 0; every finish dot `OK`.

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
