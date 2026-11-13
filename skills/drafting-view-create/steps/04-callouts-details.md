# 04 · Callouts, interior elevations, details (thang bộ, thang máy, vệ sinh, chi tiết)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1, VC2, VC8; `drafting-sheet-layout.md` §4b (CT1–CT3), §8 (TG1–TG6) · **Tools** `view_create`, `view_crop_info`, `query_elements_with_filter`
**Applies to** the creation table rows of kind callout, interior elevation, detail, drafting (thang bộ, thang máy, vệ sinh, chi tiết).

## Do

1. **Stairs** (one register row per stair, sub-rows per view, CT2):
   - a callout plan on the floor plan of each level, boxed to the core + walls to the nearest grid;
   - one section along the flights;
   - enlarged details of that section (detail callouts at 1:20–1:10): at least the foot of the stair, a flight meeting a landing, the last riser meeting the upper floor;
   - one nosing detail per tread finish (detail callout at 1:5–1:2 from an enlarged detail; a drafting view when the office has a standard one);
   - template keys `stair` · `stair-section` · `stair-detail`; full names by VC2 / TG3 (`CHI TIẾT THANG BỘ ST1 - MẶT BẰNG TẦNG TRỆT`, `… - MẶT CẮT A-A`, `… - CHI TIẾT PHÓNG LỚN MẶT CẮT 1`, `… - CHI TIẾT MŨI BẬC`);
   - the view names must also match the profile's stair view names, so that `drafting-stair-plan` / `drafting-stair-section-*` find them.
2. **Lift** (one row per lift, only when the model has one, CT3):
   - a callout plan of the shaft on each level that differs (pit, typical, top);
   - one section through the shaft, pit floor → top of the overrun;
   - template keys `vertical` · `vertical-section`; names `CHI TIẾT THANG MÁY LF1 - MẶT BẰNG HỐ THANG <tầng>`, `… - MẶT CẮT HỐ THANG`.
3. **Toilets** (one row per toilet type, CT1), always two plans:
   - the dimension plan: callout plan at 1:25 on the floor plan of its level (`… - MẶT BẰNG KÍCH THƯỚC`, key `enlarged`);
   - the floor finish plan: `kind:"duplicate"` of the dimension plan, same crop and scale (`… - MẶT BẰNG LÁT SÀN`, key `enlarged-finish`);
   - one interior elevation marker inside, one view per wall that has fixtures or tiles (`… - TRIỂN KHAI TƯỜNG <tên>`, key `enlarged-elevation`).
4. **Details** (tam cấp, bồn hoa, ramp, ban công, mái, mặt tiền, giếng trời, tiếp giáp, thu nước mưa):
   - detail section or detail callout from the plan / section / elevation where the part shows;
   - a standard detail not taken from the model → drafting view;
   - template `detail`, scale of the register.
5. `view_create`, `preview` → `apply` with `logPath`:
   - callout plan: `kind:"callout"`, `sourceViewId` = the floor plan, `box {min, max}` (model mm);
   - detail callout: `kind:"detail"`, `sourceViewId` = the plan / section / elevation, `box` (in a section give `z` too);
   - stair, lift, detail sections: `kind:"section"`, `line`;
   - stair enlarged details and nosing details: `kind:"detail"`, `sourceViewId` = the stair section (or the enlarged detail), `box` with `z`;
   - the toilet floor finish plan: `kind:"duplicate"`, `sourceViewId` = the dimension plan;
   - interior elevations: `kind:"elevation"`, one item per wall, `viewType` = the project's interior elevation type;
   - drafting views: `kind:"drafting"`.
6. Write the ids into the register sub-rows.

## Done when

- Every callout / detail sub-row has a view of the right kind, from the right parent view, with name, template and scale.
- Every stair has a plan view per level it serves + at least one section + its enlarged section details + a nosing detail (CT2).
- Every toilet type has its two plans (dimension, floor finish) + its interior elevations (CT1).
- Every lift of the model has its shaft plans + its shaft section (CT3).
- No view name holds an abbreviation (`MB`, `MC`, `WC`…) or lacks its object (TG1–TG4).
- Every view of a stair, lift or toilet carries the same component code as its sheet (`ST1`, `LF1`, `WC1`…); a kind the building has only one of carries no code (TG6).

## Skip only if

The table has no callout or detail row.

## Never

- Use the crop of a floor plan as a "stair view" instead of a callout (VC8).
- Put the toilet dimensions and the floor finish in one plan (CT1).
- Draw the build-up of a nosing or an enlarged detail yourself: the view, crop and name are yours, the build-up comes from the office library (level 1).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
