# 03 · SA3 Landings (dim chiếu nghỉ)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SA3 · **Tools** `dims_rail_refs`, `dims_at_positions`, `dims_text`, `export_sheet_images`, `view_elem_boxes`, `stair_plan_audit`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_dims-common.md`

## Do

Inside the core, **along the well axis**, on the mid landing and on the floor landing: a chain closed on the finish wall `gap | handrail | landing clear` (e.g. `49 | 80 | 1600`, `1393 | 80 | 2618`), no CLEAR.

- **The SA3 end = the handrail end drawn on the view** (the rounded end of the centre handrail), not the first edge found and not a fixed "riser ± 80" (seen: −80 on one landing, +120 on the other).
- Probe the edges (`dims_rail_refs mode:"probe"`), export the view at 6000 px, crop the landing, pick the edge on that rounded end. The audit's `Clear` is a hint only.
- Handrail references: as card 02.

## Done when

- `stair_plan_audit {viewIds, rules:["SA3"]}`: SA3 `OK` on the mid landing and the floor landing.
- The end checked on the image against the drawn handrail end.
- Every new dim drawn (Box ≠ null).

## Skip only if

Never (each seen landing).

## Never

- Taking the SA3 end from the audit without checking the image.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
