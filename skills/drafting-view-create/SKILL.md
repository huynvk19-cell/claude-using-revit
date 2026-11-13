---
name: drafting-view-create
description: "Create the views the sheets need, from the sheet register (tạo view cho sheet): map each drawing to its Revit view (floor / ceiling plan, duplicate, dependent per zone, elevation, section, callout, interior elevation, detail, drafting, legend, schedule), template, scale, crop, name; reuse existing views; check. Use for tạo view, tạo mặt bằng, tạo mặt đứng, tạo mặt cắt, tạo callout, view cho sheet, dependent view."
---

# Create views (tạo view cho sheet)

**Tools** (load all in one ToolSearch call): `view_create`, `crop_side_to_grids`, `query_elements_with_filter`, `sheet_browser_names`, `view_crop_info`.

Every creating card uses `view_create {items, mode}` (contract: the standard §3): `preview` first, then `apply` with `logPath`, up to 10 views per call. `Reused` = a view of that name already exists: take its id, nothing is created (VC1). An item that comes back as `Error` and cannot be fixed from the profile goes to the user as one row (kind, from, level, template, scale, name).

Standard: `~/.claude/drafting-domain/drafting-view-create.md` (Vietnamese): table §1 drawing → Revit view, rules VC1–VC12.

**Input**: the sheet register (`drafting-sheet-layout.md` §11), rows in state `sheet`.
**Output**: `Loại view`, `Tên view`, `View id`, `Template`, `Crop` filled, rows → `view`.

Load `drafting-session` first. Take these values from the project `drafting-profile.md`:
- the view template name for each key of table §1 (`plan`, `plan-finish`, `ceiling`, `roof`, `site`, `site-setout`, `elevation`, `section`, `stair`, `toilet`, `detail`, `legend`);
- the view name pattern, the level names (hầm, trệt, lầu…), the reference grids;
- phase, the view parameter that groups views in the Project Browser, the zone scope boxes.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every row of the register before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-view-list.md` | View list from the register, reuse existing (danh sách view cần tạo) |
| 01 | `steps/01-templates.md` | Templates per view kind (template theo loại view) |
| 02 | `steps/02-plans.md` | Plans: floor, finish, roof, ceiling, site (mặt bằng) |
| 03 | `steps/03-elevations-sections.md` | Elevations and sections (mặt đứng, mặt cắt) |
| 04 | `steps/04-callouts-details.md` | Callouts, interior elevations, details (thang bộ, thang máy, vệ sinh, chi tiết) |
| 05 | `steps/05-zones.md` | Dependent views per zone (view theo vùng) |
| 06 | `steps/06-legends-schedules.md` | Legends, drafting views, schedules (legend, bảng thống kê) |
| 07 | `steps/07-verify.md` | Verify and update the register (kiểm, cập nhật sổ) |

A card says: what to do (commands), **done when**, **skip only if**, never.

## Never

- Create a view that has no row in the register, or a second view for a row that already has one (VC1).
- Leave a view without a view template, or create / edit a template yourself (VC3).
- Edit the model to get a view (R1); delete a view someone else made.
- Place views on sheets here (that is `drafting-sheet-place`).
- Write C# or run ad-hoc code to create views (a command is added through `revit-commands/` only).
