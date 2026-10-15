# 04 · SA2 Run lengths (dim chiều dài vế)

**Skill** `drafting-stair-plan` · **Standard** `drafting-stair-plan.md` SA2 · **Tools** `dims_at_positions`, `dims_text`, `view_elem_boxes`
**Applies to** every plan view of the job, one view per call.
**Read first** (once per job): `_dims-common.md`

## Do

Outside each side wall, **outside the SB1 run tags**: the chain `landing | formula | floor landing | (grid) | …`, then the overall (no CLEAR).

```
dims_at_positions {viewId, mode:"preview", use3D:true, typeName:<check type>,
  dims:[{name:"SA2 bottom", measure:"right", lineMm:<line>, positions:[<grid>, {mm:<wall finish>, src:"3d"}, {mm:<run end>, id:<STAIRS id>, src:"view"}, {mm:<run start>, id:<STAIRS id>, src:"view"}, <grid>, {mm:<wall finish>, src:"3d"}]}]}
```

- Formula text with `dims_text`: prefix from the audit's `Text` (`280mm x 14T =`), below `(EQUAL TREADS)`. One spelling for the project.
- A grid crossing the core goes into the chain; the audit accepts the split (`OK (split)`).
- One side only: the side the audit's `Lanes` gives (`DimSide`).

## Done when

- `stair_plan_audit` SA2 `OK` (or `OK (split)`) for every lane, prefix right.
- The chain outside the run tags, the overall outermost, lines 7 paper mm apart.
- Every new dim drawn (Box ≠ null).

## Skip only if

Never.

## Never

- The same length dimmed on both sides of the core.
- Counting the flush top tread (16R → 15T).
- Spellings like `280x13T= ` (rewrite to `280mm x 13T =`).
