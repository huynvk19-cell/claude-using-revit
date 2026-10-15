# 11 · SB6 Wall finish W.. (hoàn thiện tường)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB6 · **Tools** `annot_place`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_tags-common.md`

## Do

- The mark the project already uses (`W..`), one per wall finish kind seen, usually one per wall face.
- Code from the room's Wall Finish parameter, the finish legend, or the finished views of the same core.
- Leader to the nearest wall face; heads may gather in a free inside corner of the core.

## Done when

Check: `stair_plan_audit {viewIds, rules:["SB6"]}` (`Tags.Walls, Tags.FinishMarks`, SB6 issues).

- At least one `W..` per wall finish kind seen, leader touching the finish face.

## Skip only if

**Never** (mandatory; error of 2026-10-07: skipped as "the project does not use it"). Unknown code → ask the user.
