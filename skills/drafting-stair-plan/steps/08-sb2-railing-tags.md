# 08 · SB2 Railing tags (tag lan can P01 / P02)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB2 · **Tools** `annot_place`, `tag_leaders_info`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_tags-common.md`

## Do

- `P01` on each wall handrail, `P02` on the centre railing (the project's codes).
- Head off the run lines (stair well, landing or beside the railing), short orthogonal leader to the railing.

## Done when

Check: `stair_plan_audit {viewIds, rules:["SB2"]}` (`Tags.Railings`, SB2 issues).

- One tag per railing (each wall handrail its own `P01`).
- No head on the tread lines, even where the sample has them there.

## Skip only if

A handrail hidden by a bigger railing (not seen on the view).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
