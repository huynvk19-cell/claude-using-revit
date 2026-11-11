# 03 · Place the views (đặt view)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP5–VP7, VP9 · **Tools** `viewport_place`, `sheet_views_ids`, `set_viewport_type`
**Applies to** every ready row of card 00.

## Do

1. Place each view on its sheet at its `Vị trí` with `viewport_place`, one call per batch of up to 10 views, `preview` → `apply` with `logPath`:
   - single views: `centerMm {x, y}`;
   - plan series: `gridPoint {gridA, gridB, atMm: P}` with the two reference grids, the same `P` on every sheet of the series (VP5);
   - elevations / sections that share a sheet: `gridPoint {gridA, level, atMm}` so that ±0.000 and the grid line up (VP6);
   - `viewportType` = the profile's type for the view's kind.
2. Read the result:
   - `Error: view is already on sheet …` → the register is wrong for that row (LO11): fix the row, never take the view off the other sheet;
   - `Reused` → the view was on the sheet already: card 04 moves it if it is off;
   - `Outside` / `Overlaps` → back to card 01 for that sheet.
3. `Ids` → `Viewport id` into the register, with the `Viewport type`.
4. A viewport that was on its sheet before and has another type than the profile's → `set_viewport_type` (preview → apply with `logPath`).

## Done when

- Every ready row has a `Viewport id` on its planned sheet, with the profile's viewport type.
- No view is on two sheets (LO11).

## Skip only if

No row is ready (card 00).

## Never

- Place on a sheet that is not the row's `Sheet id`.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
