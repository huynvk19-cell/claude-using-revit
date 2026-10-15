# 04 · LA3 Flight goings (dim chiều dài vế)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA3 · **Tools** `stair_section_annotate`, `dims_text`, `dims_text_move`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LA3"], dimTypeName:<check type>}  → apply with logPath
```

- One chain per **cut** flight at about mid-height of that flight (sample layout: 30 % of the flight height): wall finish → [landing] → first riser → [flight: formula] → last riser → [landing] → wall finish (`1150 | 280mm x 15T = 4200 | 1150`).
- Treads T = R − 1 (the top tread level with the landing belongs to the landing segment): 10R → `280mm x 9T = 2520`.
- The wall-to-wall overall once, on the lowest flight.
- Formula text on riser numbers or on the flight → `dims_text_move` (alongMm) into the free space under the flight.

## Done when

- `stair_section_info` LA3 rows `OK` for every cut flight, prefix `…T =` and below `(EQUAL TREADS)`.
- No chain through a run tag or the riser numbers.

## Skip only if

A flight seen beyond with the same going as a cut one (not dimmed twice).
