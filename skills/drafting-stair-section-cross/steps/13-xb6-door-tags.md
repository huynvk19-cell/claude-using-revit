# 13 · XB6 Doors (tag cửa)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XB6 · **Tools** `elevation_opening_tags`, `annot_place`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).

## Do

As LB6 (`drafting-stair-section-parallel`, card 13): `elevation_opening_tags {viewIds:[id], mode:"preview", addLeader:true}` → apply; then `annot_place` for doors in cut walls (head outside the wall, horizontal leader). Every door tag has a leader.

## Done when

- Exactly one tag per door seen or in a cut wall, all with leaders.

## Skip only if

The section shows no door and cuts no wall with a door.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
