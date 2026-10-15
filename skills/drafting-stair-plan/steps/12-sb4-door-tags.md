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
