# 07 · XA6 Handrail height (cao tay vịn) ★

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA6 · **Tools** `rail_top_at`, `dims_copy_refs`, `view_elem_boxes`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

On **every cut landing / floor** with a handrail seen: finish top → **top of the handrail**, one dim per handrail clearly seen there.

- Round values, 900–1200 (user, 2026-10-15): handrail on a wall **900**; rail at an opening (well, open landing edge) **1200**.
- References: landing / floor top on the Stairs element or Floor; the **Handrails** of the rail. From a hand dim of the same rail (`dims_copy_refs`) or ask the user for one.
- Next to the handrail, not over the XA4 chain.
- Odd value (e.g. 1188) → check with `rail_top_at`, pick another edge; still odd → Cần xem (no model edit, no Replace with text).

## Done when

- Every cut landing / floor with a seen handrail has its dims, values round, drawn (Box ≠ null).

## Skip only if

**Never** (mandatory, user 2026-10-15). A handrail hidden by a bigger railing gets no dim.

## Never

- Dim the railing frame instead of the handrail top.
