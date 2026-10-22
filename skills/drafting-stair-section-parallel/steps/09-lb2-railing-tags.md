# 09 · LB2 Railing tags (tag lan can)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB2 · **Tools** `annot_place`, `view_dims_snapshot`, `tag_leaders_info`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).

## Do

- One tag per **cut** flight for the railing seen, at the bottom end of the flight, head just above the rail, vertical leader.
- A wall handrail **P01 seen clearly** along a landing (horizontal on the wall): its own tag, head ~220–240 mm above the rail top, vertical leader to the top of P01, x between the 900 dim and the F../C.. column — on the landings without a P02 tag nearby.
- `annot_place` (kind tag, the railing tag type used in the project, orthogonal leader); view-frame `up` = elevation + the frame offset of the view (a level's witness in `view_dims_snapshot`).

## Done when

- One tag per cut flight + one per clearly seen P01 run; heads off the tread lines.

## Skip only if

A handrail hidden by a bigger railing (e.g. P01 on the wall behind the 1200 railing): no tag.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
