---
name: drafting-sheet-create
description: "Create the sheets of the set in Revit from the sheet register (khởi tạo sheet, tạo sheet theo danh mục điển hình): group drawings into sheets, number and name them, check the title block, create them through the drawing-list schedule (print set + No. follow), fill sheet parameters, check the Project Browser. Use for tạo sheet, khởi tạo sheet, thêm sheet, đánh số sheet, đặt tên sheet."
---

# Create sheets (khởi tạo sheet)

**Tools** (load all in one ToolSearch call): `cur_task`, `sheet_browser_names`, `sheet_views_ids`.

Standard: `~/.claude/drafting-domain/drafting-sheet-create.md` (Vietnamese, SC1–SC10); numbers and names follow `drafting-sheet-layout.md` §3, §8.

**Input**: the sheet register (`drafting-sheet-layout.md` §11), rows `planned` after `drafting-sheet-layout` card 03.
**Output**: `Số sheet`, `Tên sheet`, `Sheet id` filled, rows → `sheet`.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- drawing-list schedule / print set; sheet number prefix and format;
- title block type; common sheet parameters and their values (stage, issue date, drawn / checked / approved by);
- the sheet parameter that groups sheets in the Project Browser, and its values per group.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every row of the register before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-read-register.md` | Read the register, gate the rows (đọc sổ, lọc dòng) |
| 01 | `steps/01-group-number-name.md` | Group into sheets, number, name (gom sheet, đánh số, đặt tên) |
| 02 | `steps/02-title-block.md` | Title block check (kiểm khung tên) |
| 03 | `steps/03-create.md` | Create the sheets (tạo sheet) |
| 04 | `steps/04-sheet-params.md` | Sheet parameters, Project Browser group (tham số sheet) |
| 05 | `steps/05-verify.md` | Verify and update the register (kiểm, cập nhật sổ) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Create a sheet that has no row in the register (SC1).
- Renumber, rename, retitle or delete an issued sheet (SC9).
- Create sheets outside the drawing-list schedule (SC6).
- Pass `force:true` without the user's yes.
