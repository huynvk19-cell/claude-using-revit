# 11 · LB4 Floor finish F.. (hoàn thiện sàn)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB4 · **Tools** `finish_mark_place`, `annot_copy_in_view`, `finish_marks_snap`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

- `F..` is a **Generic Annotation** box (same family as the wall finish marks), not a tag.
- One above each landing / floor that has an LB3 spot, leader down to the finish top: copy one from the view (`annot_copy_in_view`) or `finish_mark_place`, then `finish_marks_snap {mode:"survey"}` → `preview` → `apply` (dot on the top).

## Done when

- One `F..` per landing / floor with a spot; `finish_marks_snap` survey `OK` (dot on the top face).

## Skip only if

Never. Unknown code → ask.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
