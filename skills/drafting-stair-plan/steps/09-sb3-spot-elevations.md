# 09 · SB3 Spot elevations (cao độ)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SB3 · **Tools** `annot_place`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_tags-common.md`

## Do

`annot_place` kind `spot` on the **Stairs** element (its plan face at the point), as the project sheets do:
- on every seen landing (sample `2625`);
- on the floor landing inside the core (sample `5250`, `0.000`);
- **when the plan cuts the stair door**: on the floor just **outside** the door, **even at the same level** — it tells inside from outside the stair enclosure on the escape route (link floor → link reference).

## Done when

Check: `stair_plan_audit {viewIds, rules:["SB3"]}` (`Spots`, SB3 issues).

- A spot on every seen landing and the floor landing; one outside each cut stair door.
- None on the door swing or on the path arrow.

## Skip only if

Outside the door only: the plan does not cut the stair door.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
