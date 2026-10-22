# 02 · LA1 Flight heights (dim chiều cao vế)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA1 · **Tools** `stair_section_annotate`, `dims_text`, `view_elem_boxes`, `stair_section_info`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LA1"], dimTypeName:<check type>, la1LineMm}  → apply with logPath
```

- One chain outside **one** wall (sample: right), finish top of floor / landing → next landing / floor, continuous over the section height; one segment per flight.
- Text: prefix `169.4mm x 16R = `, below `(EQUAL RISERS)` (from `RiseText`), value live.
- Sample layout: LA1 300 mm inside an existing overall dim.

## Done when

Check: `stair_section_info {viewId, rules:["LA1"]}` (only LA1 rows, notes, issues).

- `stair_section_info` LA1 rows `OK` for every flight.
- R × riser height = segment value (± 1 mm from rounding); a bigger gap → wrong reference or model: report it.

## Skip only if

Never.

## Never

- LA1 and LA2 on the same side.
- Replace with text.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
