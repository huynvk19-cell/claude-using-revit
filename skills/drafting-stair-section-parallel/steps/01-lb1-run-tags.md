# 01 · LB1 Run tags (tag vế)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB1 · **Tools** `stair_section_annotate`, `stair_section_info`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LB1"], runTagTypes:[{fromMm, type}]}  → apply with logPath
```

- One tag per **cut** flight (user, 2026-10-08), head in the free space under the flight (sample layout: head at 8 % of the flight height), short horizontal leader to the soffit.
- Run tag type per floor block (`runTagTypes`): the text must read the flight's absolute `From EL … To EL …`; wrong → the next type, never guess from the name.

## Done when

Check: `stair_section_info {viewId, rules:["LB1"]}` (only LB1 rows, notes, issues).

- One tag per cut flight, text `From EL … To EL … / …R` right (sections write risers `R`, plans treads `T`).
- No tag on a flight seen beyond; no tag over a dim, a number or the tread lines.

## Skip only if

Never.

## Never

- Tag the flights seen beyond.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. Ask the user for the deductions of this card when they review it, then write them here.
