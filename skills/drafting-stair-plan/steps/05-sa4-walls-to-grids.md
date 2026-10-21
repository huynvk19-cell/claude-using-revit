# 05 · SA4 Walls, doors, windows → grids (tường/cửa → trục)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SA4 · **Tools** `dims_at_positions`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_dims-common.md`

## Do

Outside the end walls: grid → wall faces → door/window edges → grid; then the overall.

- Wall faces from 3D (`src:"3d"`), doors and windows from their Left / Right references, grids `new Reference(grid)` (host grids only).

## Done when

- `stair_plan_audit {viewIds, rules:["SA4"]}`: SA4 `OK` at each end wall.
- Every new dim drawn (Box ≠ null).

## Skip only if

The end wall has no grid, no door and no window seen (audit `Walls`): then a wall-to-wall chain only, say so.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
