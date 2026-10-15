# 08 · LC Riser numbers (số bậc)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LC · **Tools** `stair_section_annotate`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LC"], numberSourceId:<a finished NumberSystem>}  → apply with logPath
```

- On each **cut** flight only, settings copied from `numberSourceId`.
- Continuous over the whole stair from the first riser of the lowest floor (sample F1 1…16, F2 17…31…), every second number (odd), next to the nosings, above the treads.

## Done when

- Every cut flight numbered; the same number as the plan at the same riser (`drafting-stair-plan`, card 06).
- Cross-check: R × riser height = LA1, T × depth = LA3.

## Skip only if

Never. Wrong start number → fix by hand or ask (a Stairs property, R1).

## Never

- Number the risers of a flight seen beyond.
