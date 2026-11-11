---
name: drafting-sheet-layout
description: "Plan the sheet set (layout bản vẽ, dàn trang, bộ hồ sơ) for the building type: nhà liên kế, biệt thự, nhà cao tầng, sân bay, trường học, bệnh viện, TTTM, nhà xưởng. Set type + stage, build the sheet register (bản vẽ cần có), compare with the model, plan scale / paper / zones, check the finished layout (LO / Z). Sheets, views, placing: drafting-sheet-create, drafting-view-create, drafting-sheet-place. Use for bộ hồ sơ cần có, thiếu bản vẽ, chia vùng, kiểm layout."
---

# Sheet layout (layout bộ hồ sơ)

**Tools** (load all in one ToolSearch call): `cur_task`, `export_sheet_images`, `query_elements_with_filter`, `sheet_browser_names`, `sheet_views_ids`, `view_crop_info`, `viewport_title_audit`.

Standard: `~/.claude/drafting-domain/drafting-sheet-layout.md` (Vietnamese), plus **one** sheet-set file for the building type, chosen in card 00 (`drafting-sheet-set-<type>.md`, table §1 of the standard). Never open the sheet-set files of other types. Card 01 also reads `~/.claude/drafting-domain/drafting-coverage.md` (kind → skill, level).

**Source of truth**: the **sheet register** (`drafting-sheet-layout.md` §11), one row per drawing.
- This skill creates the register (card 01) and closes it (card 04).
- In between, `drafting-sheet-create`, `drafting-view-create` and `drafting-sheet-place` each fill their own columns.

Load `drafting-session` first. Take these values from the project `drafting-profile.md` (blank → card 00 asks once, then writes them in):
- building type (+ mix, sub-type) and design stage (YT / CS / KT / TC / HC);
- paper size, title block type, drawing area (margin, title strip and notes column in mm);
- sheet number prefix, number format per group, house types / blocks codes;
- drawing-list schedule / print set;
- zone scope boxes and order (by level then zone, or zone then level), sheet name language;
- viewport gap on paper (default 15 mm).

**Scope**: which drawings the set needs (the register), scale / paper / zones, and the final layout check. Other parts go to their own skills:
- creating sheets → `drafting-sheet-create`;
- creating views → `drafting-view-create`;
- putting views on sheets → `drafting-sheet-place`;
- crops, grid ends, viewport types, drawing list / print set → `drafting-views-sheets`;
- titles → `drafting-tags-titles`;
- dims and tags inside the views → their own skills.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every row of the register before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-project-type.md` | Building type and stage (loại công trình, giai đoạn) |
| 01 | `steps/01-target-set.md` | Sheet register: drawings the set needs (sổ bộ hồ sơ) |
| 02 | `steps/02-gap.md` | Compare with the model (đối chiếu model: thiếu / thừa) |
| 03 | `steps/03-scale-paper-zones.md` | Scale, paper fit, zones (tỷ lệ, vừa khổ, chia vùng) |
| 04 | `steps/04-layout-check.md` | Layout check (kiểm layout LO / Z), closes the register |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Delete, renumber or rename a sheet already issued (HS3) without the user's yes.
- Delete a view, or take a view off a sheet, that someone else placed.
- Decide the building type, the stage, the paper size or the title block yourself.
- Edit the model (walls, rooms, levels, grids, scope boxes) to make a view fit (R1).
- Read the sheet-set file of another building type "to compare".
