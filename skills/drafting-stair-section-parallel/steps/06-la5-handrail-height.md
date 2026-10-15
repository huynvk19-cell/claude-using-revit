# 06 · LA5 Handrail height (cao tay vịn) ★

**Skill** `drafting-stair-section-parallel` · **Standard** `drafting-stair-section-parallel.md` LA5 · **Tools** `stair_section_annotate`, `rail_top_at`, `view_elem_boxes`
**Applies to** every section along the flights of the job, one view per call (`timeoutSeconds`).
**Read first** (once per job): `_annotate-common.md`

## Do

Finish top of the landing / floor → **top of the handrail** along the landing edge, on **every cut landing**. Round values, 900–1200 (user, 2026-10-15): a rail at an opening (well, open landing edge, P02) **1200**; a handrail on a wall (P01) **900**.

```
stair_section_annotate {viewId, mode:"preview", parts:["LA5"], la5RailingIds:[<P02…>], la5TargetMm:1200, la5LeftX, la5RightX}  → apply with logPath
```

- `la5Auto` (on unless `la5FromDimId` is given) finds the top-rail reference without a sample; landings with a rail dim are skipped.
- An `Errors` line `LA5 …` = that landing has no dim yet: fix it now, do not move on.
- Value far from the round height (e.g. 1188, 1329) → check with `rail_top_at`, pick another edge; unsure → do not place, ask the user for ONE hand dim, then `parts:["LA5"], la5FromDimId:<that dim>, la5IndexFrom, la5IndexTo, items:[{part:"LA5", landing, x}]` reuses its rail reference until the value matches and the dim is drawn (`skip:true` for the landings the user dimmed).
- **Wall handrail P01 seen along a landing** (horizontal on the wall, not hidden by P02): a second dim, landing top → top of the P01 handrail (`900`), 230–300 mm from the 1200 dim toward the wall (or ~500 mm from the wall finish on the other side). `la5Auto` reads top rails only: take the P01 handrail reference from a hand dim (`la5FromDimId`, `la5TargetMm:900`).

## Done when

- `stair_section_info`: one LA5 row per cut landing, all `OK`, no `LA5: landing … missing (mandatory)`.
- A 900 dim on every landing where P01 is seen along it (`Notes` line `LA5: landing … one rail dim` checked).
- Every value round; every dim drawn (Box ≠ null).

## Skip only if

**Never** (mandatory, user 2026-10-08). A rail the tool cannot reach → Việc tồn + ask for one hand dim.

## Never

- Report a section done while a cut landing has no LA5 dim.
- Dim the railing frame instead of the handrail top.
