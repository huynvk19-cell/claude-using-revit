# 05 · LA4 Clear height under landings (thông thuỷ dưới chiếu nghỉ)

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA4 · **Tools** `stair_section_annotate`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

```
stair_section_annotate {viewId, mode:"preview", parts:["LA4"], dimTypeName:<check type>}  → apply with logPath
```

- Finish top of the landing / floor → soffit of the landing (or flight) just above.
- Inside the core next to the walls, one column per side (same x on every floor), one dim per landing.
- `CLEAR` suffix only if the profile says so (default: no, as the sample).

## Done when

- One dim per landing, in a straight column per side; values = model.

## Skip only if

Never.
