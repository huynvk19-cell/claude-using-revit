# 01 · XB1 Run tags (tag vế, cả hai nửa)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XB1 · **Tools** `annot_place`, `tags_retype`, `stair_xsection_info`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_tags-common.md`

## Do

- One tag per **seen** flight, **both halves** (`away` and `toward`).
- Text box **vertical**, outside the wall of the half holding the flight (left half → outside the left wall, right half → right wall), level with the middle of the band; **horizontal leader** into the middle of the flight.
- `annot_place` (kind tag, the section run tag type of that floor block). Wrong `From EL / To EL` → wrong floor-block type: `tags_retype`.

## Done when

Check: `stair_xsection_info {viewId, rules:["XB1"]}` (only XB1 rows, notes, issues).

- One tag per seen flight in both halves, text `…R` / `From EL … To EL …` right.
- No tag over a dim or another tag.

## Skip only if

Never.
