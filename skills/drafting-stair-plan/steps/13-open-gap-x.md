# 13 · Open gap between flights (X thông tầng)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` §0 "Lỗ trống cạnh vế thang" · **Tools** `stair_gap_x`, `detail_lines_set`
**Applies to** every plan view of the job, all in one call (`viewIds`).

## Do

The well between two flights open to the floors below (often kept for the fire-fighting pipes) is shown with an X in `<Overhead>` dashed lines fitting its 4 corners.

```
stair_gap_x {viewIds:[<all plans>], mode:"preview"}  → read Gaps (corners, size, bound by, skipped)
stair_gap_x {viewIds:[<all plans>], mode:"apply", logPath}
```

- It draws the missing diagonals and refits an existing X; `undo` with the log.
- A gap it misses or gets wrong → pass `rect:[r0,u0,r1,u1]` (view mm). One line to adjust by hand: `detail_lines_set`.
- Tag heads it lists in `Warnings` → move them off the X (to the landing, orthogonal leader).

## Done when

Check: `stair_gap_x {viewIds, mode:"preview"}` again: every gap `fits` or `Skipped`, nothing to create.

- Every open gap has one X, corners on the inner railing frame / run / landing edges (checked on the image), no second X.
- No tag head on the X.

## Skip only if

The side of the flight is a solid wall or a floor (the command reports it as `Skipped`).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
