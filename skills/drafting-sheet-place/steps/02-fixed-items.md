# 02 · Notes, legends, key plan, north arrow, schedules (phần cố định)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP4, VP8; `drafting-sheet-layout.md` LO8, Z3 · **Tools** `sheet_views_ids` · **Planned** `viewport_place`
**Applies to** the sheets of the job.

## Do

1. On every sheet, at the spots of card 01:
   - the notes / legend column;
   - the key plan on zoned sheets (Z3, the zone shaded);
   - the north arrow on every plan sheet, same corner;
   - the schedules on their sheets.
2. Legends may go on many sheets; a schedule goes on one sheet.
3. With `viewport_place`: one call per batch of sheets, `preview` → `apply` with `logPath`.

   Without it: hand the user the list (sheet → item → point mm). Read back with `sheet_views_ids {sheetPrefix, includeAll:true}`.

## Done when

- Every sheet of the job has the fixed items its kind needs, at the planned spots (readback).

## Skip only if

The profile and the register name no fixed item for these sheets.

## Never

- Let a legend or schedule overlap the title block.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
