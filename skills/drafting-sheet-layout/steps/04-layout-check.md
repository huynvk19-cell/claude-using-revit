# 04 · Layout check (kiểm layout LO / Z)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §6, §7, §9 · **Tools** `sheet_views_ids`, `viewport_title_audit`, `export_sheet_images`, `cur_task`
**Applies to** the sheets of the job.

## Do

1. Read-only, in one pass over all the sheets of the job:
   - `sheet_views_ids` per prefix: views, viewport types, scales;
   - `viewport_title_audit {sheetPrefix}`: titles (LO7);
   - `cur_task {task:"print_sets_status"}`: drawing list = print set (HS6);
   - the sheet images of `drafting-sheet-place` card 05 (export again only if a sheet changed since).
2. Score each sheet against the rules. The checks are by image (`drafting-visual-checker` reads the images; ask it for these rules by code) and by the `Vị trí` column of the register:
   - LO1: inside the drawing area;
   - LO2: one main scale;
   - LO3: reading order;
   - LO4: same point across the series;
   - LO5: rows aligned;
   - LO6: gaps, no overlap;
   - LO8: notes / key plan / north arrow place;
   - LO9: detail grid;
   - LO10: density;
   - Z2 / Z3: matchlines + key plans.
3. LO12: list the section / callout / elevation marks that point at views not on a sheet. They go to Việc tồn unless the view is in the register.
4. Report one row per sheet:

   | Sheet | Failed rules | What | Fix |
   |---|---|---|---|

   Fixes go back to `drafting-sheet-place` (position) or `drafting-sheet-create` (grouping, numbers). Then check again.
5. Fill `Kiểm` and set every passing row to `checked` in the register (§9, §11).

## Done when

- Every sheet of the job passes LO1–LO12, and Z1–Z5 if zoned.
- `viewport_title_audit` is clean and the print set is `inSync`.
- Every register row is `checked`, `n/a`, or `!` under Việc tồn with its reason (§9).

## Skip only if

Never.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
