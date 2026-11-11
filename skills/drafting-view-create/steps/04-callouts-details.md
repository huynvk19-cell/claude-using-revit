# 04 · Callouts, interior elevations, details (thang, WC, chi tiết)

**Skill** `drafting-view-create` · **Standard** `drafting-view-create.md` §1, VC2, VC8 · **Tools** `view_crop_info`, `query_elements_with_filter` · **Planned** `view_create`
**Applies to** the creation table rows of kind callout, interior elevation, detail, drafting (thang bộ, thang máy, WC, chi tiết).

## Do

1. **Stairs, lift** (one register row per stair, sub-rows per view):
   - a callout plan on the floor plan of each level, boxed to the core + walls to the nearest grid;
   - one section along the flights (lift: through the shaft, pit floor → top of the overrun);
   - template `stair`, names by VC2 (`CHI TIẾT THANG BỘ 1 - MB TRỆT`, `… - MẶT CẮT 1`);
   - the view names must also match the profile's stair view names, so that `drafting-stair-plan` / `drafting-stair-section-*` find them.
2. **Toilets** (one row per WC type):
   - callout plan at 1:25 on the floor plan of its level;
   - one interior elevation marker inside, one view per wall that has fixtures or tiles;
   - template `toilet`.
3. **Details** (tam cấp, bồn hoa, ramp, ban công, mái, mặt tiền, giếng trời, tiếp giáp, thu nước mưa):
   - detail section or detail callout from the plan / section / elevation where the part shows;
   - a standard detail not taken from the model → drafting view;
   - template `detail`, scale of the register.
4. With `view_create`: `preview` → `apply` with `logPath`.

   Without it: hand the user the table:

   | Tên | Loại | Từ view | Vùng / qua | Tỷ lệ | Template |
   |---|---|---|---|---|---|

   Read back with `query_elements_with_filter`.
5. Write the ids into the register sub-rows.

## Done when

- Every callout / detail sub-row has a view of the right kind, from the right parent view, with name, template and scale.
- Every stair has a plan view per level it serves + at least one section; every WC type has its plan + its interior elevations.

## Skip only if

The table has no callout or detail row.

## Never

- Use the crop of a floor plan as a "stair view" instead of a callout (VC8).

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
