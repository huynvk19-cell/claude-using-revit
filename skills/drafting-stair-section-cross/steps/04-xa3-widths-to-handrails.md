# 04 · XA3 Widths to handrails (bề rộng vế tới tay vịn)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XA3 · **Tools** `dims_copy_refs`, `view_elem_boxes`, `stair_xsection_info`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_dims-common.md`

## Do

- Chain: wall finish → handrail edge → handrail edge → centre wall face → centre wall face → handrail edge → handrail edge → wall finish (sample `70 | 1745 | 70 | 320 | 70 | 1745 | 70`): the clear width between handrails and what the handrails take.
- **One chain per stair block** (each part of the core with another width or other handrails; sample: three). A half without a handrail in that block → the chain goes to the wall (`1885 | 320 | 70 | 1745 | 70`).
- In the free space above a landing, not through a tag, numbers or an F../C.. box.
- References: wall faces, **Handrails**, Stairs edges; never a link.

## Done when

Check: `stair_xsection_info {viewId, rules:["XA3"]}` (only XA3 rows, notes, issues).

- One chain per stair block, every dim drawn.

## Skip only if

Never.
