# 01 · LB1 Run tags (tag vế)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB1 · **Tools** `stair_section_annotate`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LB1"], runTagTypes:[{fromMm, type}]}  → apply with logPath
```

- One tag per **cut** flight (user, 2026-10-08), head in the free space under the flight (sample layout: head at 8 % of the flight height), short horizontal leader to the soffit.
- Run tag type per floor block (`runTagTypes`): the text must read the flight's absolute `From EL … To EL …`; wrong → the next type, never guess from the name.

## Done when

- One tag per cut flight, text `From EL … To EL … / …R` right (sections write risers `R`, plans treads `T`).
- No tag on a flight seen beyond; no tag over a dim, a number or the tread lines.

## Skip only if

Never.

## Never

- Tag the flights seen beyond.
