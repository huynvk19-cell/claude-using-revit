# 02 · SA1 Clear width of runs (dim thông thuỷ vế)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SA1 · **Tools** `dims_rail_refs`, `dim_stable_refs`, `dims_copy_refs`, `dims_at_positions`, `dims_text`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_dims-common.md`

## Do

Inside the core, across the runs, next to **both ends** of the runs:
`70 | 1550 CLEAR | 80 | 300 | 80 | 1550 CLEAR | 70`, plus the overall as a chain closed on the walls: `handrail | overall | handrail | gap` (e.g. `80 | 2590 | 80 | 390`), no CLEAR.

Handrail references (dims the API builds on Top Rail lines are not drawn) — **no hand dim is needed**:
- `dims_rail_refs {viewId, mode:"probe" | "preview" | "apply", dims:[{measure, lineMm, positions:[{mm, rail:<top rail id>}, {mm, wall:<wall id>}, {mm, ref:"<stable>"}]}]}` builds each reference like a UI pick and keeps only edges that give the right value and are drawn. Top rail ids: `dim_stable_refs {railingIds}`.
- If the automatic edge test rejects every edge (it happens on dims measured along the view Right), take the edge from `probe` and pass it as `{ref}`; a value of `-305` means that edge is not drawable.
- A chain the user drew by hand in another view of the same railings: `dims_copy_refs {targetViewId, items:[{sourceDimId, typeName}]}` reuses its references (texts copied).
- Rails above the cut plane are never dimensionable: use a rail below the cut at the same position.

Then `dims_text`: suffix `CLEAR` on the clear width of each run, nothing else.

## Done when

- `stair_plan_audit {viewIds, rules:["SA1"]}`: SA1 rows `OK` at both ends of the runs.
- `CLEAR` only on run clear widths; bounds = railing edge, else step edge, else finish wall (never across an opening).
- Every new dim drawn (`view_elem_boxes` Box ≠ null).

## Skip only if

Never.

## Never

- `CLEAR` on the overall, the handrails, the gap or the walls.
- Measuring to a wall when a railing or a step edge comes first.
