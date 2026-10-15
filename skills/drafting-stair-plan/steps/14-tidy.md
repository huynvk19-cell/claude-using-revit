# 14 · Tidy (dọn chồng lấn)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` ĐẸP · **Tools** `annotation_overlaps`, `dims_text_move`, `tag_leaders_info`
**Applies to** every plan view of the job, one view per call.

## Do

- `annotation_overlaps {viewId}` → fix every overlap.
- `dims_text_move` for short segments whose texts overlap (`80 | 30 | 80`, `49 | 80`).
- `tag_leaders_info` → shape `D` (diagonal) = fix to one segment or one elbow.

## Done when

- `annotation_overlaps` = 0; short texts apart; no diagonal leader.
- Generic-annotation finish marks and path texts are not seen by `annotation_overlaps`: checked on the image (card 15).

## Skip only if

Never.
