# 10 · SB5 Floor finish F.. (hoàn thiện sàn)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB5 · **Tools** `annot_place`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_tags-common.md`

## Do

- The finish mark the project already uses (often a Generic Annotation box, listed by the audit as `FinishMarks`, `F..` = floor).
- On the mid landing and the floor landing, next to their spot elevation (card 09).
- Code from the room's Floor Finish parameter, the finish legend, or the finished views of the same core.

## Done when

Check: `stair_plan_audit {viewIds, rules:["SB5"]}` (`Tags.Landings, Tags.FinishMarks`, SB5 issues).

- One `F..` per landing and floor landing, grouped with its spot, off the path and the SA3 dims.

## Skip only if

**Never** (mandatory, user 2026-10-15). Unknown code → ask the user.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
