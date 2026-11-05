# 06 · Place and align views (đặt và căn view)

**Skill** `drafting-sheet-layout` · **Standard** `drafting-sheet-layout.md` §6 (LO4–LO6, LO11) · **Tools** `sheet_views_ids`, `export_sheet_images`
**Applies to** the sheets of the job.

## Do

1. **No command places or moves viewports yet.**
   - Give the user the layout plan of card 05, sheet by sheet: view → sheet → centre point / reference grid point.
   - The user drags each view from the Project Browser onto its sheet.
   - Plan series: the user aligns them with Revit's "Guide Grid" on the sheet, or by moving the viewport until the reference grid point matches.
   - When a placing command exists, this card uses it instead (preview → apply with `logPath` → undo), and its name goes on the **Tools** line.
2. Read back once the user says done: `sheet_views_ids` per prefix, then compare with the plan.

   | Sheet | View | Planned | Found | ok / off (mm) |
   |---|---|---|---|---|

3. Export the sheets of the job once (`export_sheet_images {sheetNumbers:[…]}`) for card 07 and `drafting-visual-check`.

## Done when

- Every view of the plan is on its planned sheet (`sheet_views_ids`), and no view is on two sheets (LO11).
- Plan series sit at the same reference point within 1 mm on paper (LO4), or the offset is reported to the user.

## Skip only if

Every view of the plan is already on its sheet at its planned point (card 07 clean on an earlier pass).

## Never

- Write C# or run ad-hoc code to place viewports in this card (a command is added through `revit-commands/` only).
- Move viewports placed by someone else without saying which ones and by how much.

## Score (0–10)

No deductions of its own yet: 10 when every **Done when** line holds; otherwise lower it and say which line failed. **Accepted only above 7**; below, dissect and run again (`drafting-work-rules.md` §2b2). Ask the user for the deductions of this card when they review it, then write them here.
