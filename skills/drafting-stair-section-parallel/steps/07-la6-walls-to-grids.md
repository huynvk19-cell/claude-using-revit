# 07 · LA6 Walls, doors → grids (tường/cửa → trục)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA6 · **Tools** `stair_section_annotate`, `view_elem_boxes`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

Below the section, outside the floor: grid → wall faces → door edges → … → grid; overall outside (as SA4 of the plan). When the upper walls differ from the lower ones, a second chain + overall on **top** of the section.

```
stair_section_annotate {viewId, mode:"preview", parts:["LA6"], dimTypeName:<check type>, la6LineMm?, la6Top?}  → apply with logPath
```

- Bottom chain at `la6LineMm` (default 1000 below the lowest floor): grids → both faces of each core wall → edges of the doors facing the view in the lowest storey → grids, + the overall; with `la6Top` (default on) the same on top when the upper walls differ. Host grids only.
- Skipped when a chain with grid references already sits below the lowest floor.
- Anything it misses (doors in cut walls, extra walls): a one-off dynamic command from the manual references (`_annotate-common.md`).

## Done when

- Chain + overall below (and on top when the walls differ), every dim drawn (Box ≠ null).

## Skip only if

No grid seen: the chain stops at the wall faces; say so.
