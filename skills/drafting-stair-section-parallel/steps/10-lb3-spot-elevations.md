# 10 · LB3 Spot elevations (cao độ)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LB3 · **Tools** `stair_section_annotate`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LB3"], spotTypeName}  → apply with logPath
```

- On every seen landing / floor top, **at each end** where there is a landing (sample: left and right), near the landing edge, away from the F.. box.

## Done when

- A spot on every seen landing / floor top at each end; none over an F../C.. mark.

## Skip only if

Never.
