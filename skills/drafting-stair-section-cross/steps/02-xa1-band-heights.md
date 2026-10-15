# 02 · XA1 Band heights (dim chiều cao từng dải)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA1 · **Tools** `dims_copy_refs`, `dims_edit`, `dims_text`, `view_elem_boxes`, `stair_xsection_info`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

- Outside one wall (sample: right): finish top of the lowest floor → top of the landing / floor at each end of a band → … → top landing. **One segment per band** = R × riser height.
- Text: prefix `169.4mm x 16R = `, below `(EQUAL RISERS)` (from `RiseText`), value live. The segment from the top landing to the roof (not a flight) carries no formula.
- Outermost: one overall, foot → roof.
- Merged bands in one segment (sample: `3700` = 6R + 15R) → split: `dims_edit` with log, then rebuild.

## Done when

Check: `stair_xsection_info {viewId, rules:["XA1"]}` (only XA1 rows, notes, issues).

- `stair_xsection_info` XA1 rows `OK` for every band; no merged band, no 0 mm segment.
- One overall outermost; every dim drawn (Box ≠ null).

## Skip only if

Never.

## Never

- Merge two bands in one formula segment.
- XA1 and XA2 on the same side; two identical overalls.
