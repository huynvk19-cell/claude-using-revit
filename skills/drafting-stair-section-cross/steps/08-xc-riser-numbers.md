# 08 · XC Riser numbers (số bậc Start and End)

**Skill** `drafting-stair-section-cross` · **Standard** `drafting-stair-section-cross.md` XC · **Tools** `stair_numbers_match`, `stair_xsection_info`
**Applies to** every section across the flights of the job, one view per call (`timeoutSeconds`).

## Do

- Only on `away` flights (risers seen). Remove numbers from `toward` flights.
- Create on the run (NumberSystem), then copy the settings of a finished cross-section number with `stair_numbers_match`: **Display Rule Start and End** (the middle numbers would overlap end-on), Tag Type Riser, Reference Right Quarter, Justify Back, Number Size 3.0 mm.
- Continuous by elevation over the whole core (scissor stair: a band of two flights counted once), the same number as the plan / longitudinal section at the same riser.

## Done when

- Every `away` flight numbered Start and End; none on `toward` flights.
- `FirstNumber` = `FirstByElevation` and matches the plan.

## Skip only if

Never. A start number off → compare with the plan first; changing it is a Stairs property: ask (R1).

## Never

- Display Rule Odd here.
