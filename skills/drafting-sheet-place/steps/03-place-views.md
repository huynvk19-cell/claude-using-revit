# 03 · Place the views (đặt view)

**Skill** `drafting-sheet-place` · **Standard** `drafting-sheet-place.md` VP5–VP7, VP9 · **Tools** `sheet_views_ids`, `set_viewport_type` · **Planned** `viewport_place`
**Applies to** every ready row of card 00.

## Do

1. Place each view on its sheet at its `Vị trí`:
   - with `viewport_place`: centre point, or `gridPoint` (two reference grids at `P`) for plan series; the viewport type of the profile for its kind;
   - one call per batch of up to 10 views, `preview` → `apply` with `logPath`.
2. Without `viewport_place`:
   - hand the user the plan sheet by sheet (view → sheet → point);
   - the user drags each view from the Project Browser onto its sheet, near the point;
   - then the user moves it until the point matches. Plan series: the reference grid intersection on `P`; Revit's guide grid on the sheet helps.
3. Read back: `sheet_views_ids` per prefix → `Viewport id`, `Viewport type` into the register.
4. Viewport type ≠ the profile's → `set_viewport_type` (preview → apply with `logPath`).

## Done when

- Every ready row has a `Viewport id` on its planned sheet, with the profile's viewport type.
- No view is on two sheets (LO11).

## Skip only if

No row is ready (card 00).

## Never

- Place on a sheet that is not the row's `Sheet id`.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds. **Accepted only above 7** (`drafting-work-rules.md` §2b2).
