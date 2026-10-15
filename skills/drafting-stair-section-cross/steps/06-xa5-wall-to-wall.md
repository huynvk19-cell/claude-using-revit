# 06 · XA5 Wall to wall (tường → tường)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA5 · **Tools** `stair_xsection_annotate`, `dims_copy_refs`, `view_elem_boxes`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

**Command**: `stair_xsection_annotate {parts:["XA5"]}` builds the bottom chain + overall and the one top overall; each is skipped when a horizontal dim already sits there.

- Below the section: wall outer face → finish → centre wall faces → … → outer face (sample `160 | 1885 | 320 | 1885 | 160`), overall outside (`4410`). A host grid seen → the chain goes through it (as LA6 / SA4).
- On top: **one** overall (never two identical lines).

## Done when

- Chain + overall below, one overall on top; every dim drawn; no link reference.

## Skip only if

Never.
