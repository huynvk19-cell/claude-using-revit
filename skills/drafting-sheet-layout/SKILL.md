---
name: drafting-sheet-layout
description: "Lay out the sheet set (layout bản vẽ, dàn trang, bộ hồ sơ) for the building type: nhà liên kế, biệt thự, nhà cao tầng, sân bay, trường học, bệnh viện, TTTM, nhà xưởng. Set type + stage, build the drawing list the type needs, compare with the model, plan scale / paper / zones, number and create sheets, plan and place viewports, check. Use for layout sheet, dàn sheet, bố trí bản vẽ, bộ hồ sơ cần có, thiếu bản vẽ, chia vùng, key plan."
---

# Sheet layout (layout bộ hồ sơ)

**Tools** (load all in one ToolSearch call): `cur_task`, `export_sheet_images`, `query_elements_with_filter`, `sheet_browser_names`, `sheet_views_ids`, `view_crop_info`, `viewport_title_audit`.

Standard: `~/.claude/drafting-domain/drafting-sheet-layout.md` (Vietnamese), plus **one** sheet-set file for the building type, chosen in card 00 (`drafting-sheet-set-<type>.md`, table §1 of the standard). Never open the sheet-set files of other types.

Load `drafting-session` first. Take these values from the project `drafting-profile.md` (blank → card 00 asks once, then writes them in):
- building type (+ mix, sub-type) and design stage (YT / CS / KT / TC / HC);
- paper size, title block type, drawing area (margin, title strip and notes column in mm);
- sheet number prefix, number format per group, house types / blocks codes;
- drawing-list schedule / print set;
- zone scope boxes and order (by level then zone, or zone then level), sheet name language;
- viewport gap on paper (default 15 mm).

Scope: which drawings the set needs, which sheets hold them, where each view sits on its sheet. Crops, grid ends, viewport types, drawing list / print set → `drafting-views-sheets`; titles → `drafting-tags-titles`; dims and tags inside the views → their own skills.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view / sheet of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-project-type.md` | Building type and stage (loại công trình, giai đoạn) |
| 01 | `steps/01-target-set.md` | Target drawing set (bộ hồ sơ cần có) |
| 02 | `steps/02-gap.md` | Compare with the model (đối chiếu model: thiếu / thừa) |
| 03 | `steps/03-scale-paper-zones.md` | Scale, paper fit, zones (tỷ lệ, vừa khổ, chia vùng) |
| 04 | `steps/04-sheets.md` | Number, name and create sheets (đánh số, đặt tên, tạo sheet) |
| 05 | `steps/05-layout-plan.md` | Layout plan per sheet (bố trí viewport trên từng sheet) |
| 06 | `steps/06-place-views.md` | Place and align views (đặt và căn view) |
| 07 | `steps/07-layout-check.md` | Layout check (kiểm layout LO / Z) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Delete, renumber or rename a sheet already issued (HS3) without the user's yes.
- Delete a view, or take a view off a sheet, that someone else placed.
- Decide the building type, the stage, the paper size or the title block yourself.
- Edit the model (walls, rooms, levels, grids, scope boxes) to make a view fit (R1). Missing views are asked for or listed, never invented.
- Read the sheet-set file of another building type "to compare".
