# 07 · LA6 Walls, doors → grids (tường/cửa → trục)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA6 · **Tools** `view_elem_boxes`, `dims_text`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

Below the section, outside the floor: grid → wall faces → door edges → … → grid; overall outside (as SA4 of the plan). When the upper walls differ from the lower ones, a second chain + overall on **top** of the section.

No command yet: build it as a one-off dynamic command from the manual references (`_annotate-common.md`): `new Reference(grid)`, wall side faces, door `FamilyInstanceReferenceType.Left / Right`. Host grids only.

## Done when

- Chain + overall below (and on top when the walls differ), every dim drawn (Box ≠ null).

## Skip only if

No grid seen: the chain stops at the wall faces; say so.
