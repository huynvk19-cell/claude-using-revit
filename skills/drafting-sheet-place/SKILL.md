---
name: drafting-sheet-place
description: "Put views on their sheets from the sheet register (đặt viewport): plan each sheet in paper mm, notes / legends / schedules first, plans aligned on a grid point, elevations / sections on ±0.000, check. Use for đặt view lên sheet, bỏ view vào sheet, căn viewport, dàn view trên sheet."
---

# Place views on sheets (bỏ view vào sheet)

**Tools** (load all in one ToolSearch call): `viewport_place`, `export_sheet_images`, `set_viewport_type`, `sheet_views_ids`, `view_crop_info`.

The placing cards use `viewport_place {items, mode}` (contract: the standard §3): `preview` first (its boxes and collisions are the real ones), then `apply` with `logPath`. Pass the profile's drawing area as `areaMm` and the Revit user as `me`. `Outside` / `Overlaps` in the result = the layout plan is wrong for that sheet: fix the plan (card 01), do not nudge by eye.

Standard: `~/.claude/drafting-domain/drafting-sheet-place.md` (Vietnamese, VP1–VP10); layout rules LO1–LO12, Z2–Z3 in `drafting-sheet-layout.md` §6, §7.

**Input**: the sheet register (`drafting-sheet-layout.md` §11), rows in state `view`.
**Output**: `Vị trí (mm)`, `Viewport id`, `Viewport type` filled, rows → `placed`.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- drawing area of the title block (margin, title strip, notes column, mm), viewport gap;
- viewport type per view kind; reference grids for plan series (default the first grid of each direction);
- where the notes column, the key plan and the north arrow go.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every sheet of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-ready-check.md` | Ready check: rows, crops, scales (kiểm sẵn sàng) |
| 01 | `steps/01-layout-plan.md` | Layout plan per sheet in paper mm (bảng bố trí) |
| 02 | `steps/02-fixed-items.md` | Notes, legends, key plan, north arrow, schedules (phần cố định) |
| 03 | `steps/03-place-views.md` | Place the views (đặt view) |
| 04 | `steps/04-align-series.md` | Align plan series and elevation rows (căn chuỗi mặt bằng, căn hàng) |
| 05 | `steps/05-verify.md` | Verify, images, update the register (kiểm, cập nhật sổ) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Place a view whose crop / scale is not final (VP2), or a view already on another sheet (LO11).
- Place on an issued sheet without asking; move someone else's viewport without saying which and by how much (VP9).
- Align a plan series by the viewport box centre (VP5).
- Write C# or run ad-hoc code to place viewports (a command is added through `revit-commands/` only).
