# 12 · SB4 Doors and windows (tag cửa)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB4 · **Tools** `annot_place`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_tags-common.md`

## Do

- Exactly one tag per door / window the view draws, close to it.

## Done when

Check: `stair_plan_audit {viewIds, rules:["SB4"]}` (`Openings`, SB4 issues).

- One tag per drawn opening, off the SA4 dims and off the door swing.

## Skip only if

The view draws no door or window (audit `Tags`). A door the view does not draw gets no tag.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
