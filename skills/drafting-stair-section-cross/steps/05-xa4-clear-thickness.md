# 05 · XA4 Clear height | landing thickness (thông thuỷ | dày chiếu nghỉ)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA4 · **Tools** `dims_copy_refs`, `stair_xsection_info`, `view_elem_boxes`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

- **One continuous vertical chain** inside the core, alternating `clear (landing / floor top → soffit above) | thickness (soffit → top)` → sample `2495 | 215 | 2325 | 215 | … | 5270 | 150` (last: roof soffit + roof thickness).
- It stops only at landings / floors **cut through the dim column**; a landing cut only in the other half, or seen behind, is crossed (sample `3485`, `5270`).
- In the half with the `away` flights (sample: ~2/3 of that half's width, between the numbers and the wall), one straight column over the full height. No `CLEAR` (as LA4).

## Done when

Check: `stair_xsection_info {viewId, rules:["XA4"]}` (only XA4 rows, notes, issues).

- Values = `ClearHeights.Expected` of `stair_xsection_info`, the chain complete on one half.

## Skip only if

Never.
