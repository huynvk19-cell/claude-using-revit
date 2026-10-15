---
name: drafting-views-sheets
description: "Viewports, viewport types, crops/scope boxes, dependent views, 2D grid/level ends, view templates, link display, room colour fill per view, and keeping the print set in step with the drawing list via Revit MCP. Use for viewport, đổi type viewport, crop view, scope box, đầu trục, kéo trục, bubble chồng, view template, ẩn link, V/G, tô màu room, color scheme, drawing list, danh mục bản vẽ, print set, sheet mới."
---

# Views and sheets

**Tools** (load all in one ToolSearch call): `align_grid_ends`, `crop_side_to_grids`, `grid_bubble_elbow`, `print_set_from_schedule`, `print_set_info`, `room_color_apply`, `room_color_survey`, `set_viewport_type`, `sheet_browser_names`, `sheet_views_ids`, `view_crop_info`, `view_image_trial`, `viewport_title_audit`, `viewport_titles_place`.

Standards: `~/.claude/drafting-domain/drafting-views-sheets.md`.

## Step cards

Each step is one file in `steps/` (next to this file). Open **only the card of the step you are on**. In a workflow (`drafting-workflow`), each card runs on every view of the job before the next card.

| # | Card | Step |
|---|---|---|
| 00 | `steps/00-identify.md` | Identify the views (xác định view) |
| 01 | `steps/01-viewport-type.md` | Viewport type (type viewport) |
| 02 | `steps/02-crop.md` | Crop (crop view) |
| 03 | `steps/03-grid-ends.md` | Grid ends 2D (đầu trục) |
| 04 | `steps/04-template.md` | View Template, hiding (ẩn bằng template) |
| 05 | `steps/05-link-display.md` | Link display (hiển thị link) |
| 06 | `steps/06-titles-after.md` | Titles after changes (đặt lại title) |
| 07 | `steps/07-drawing-list.md` | Drawing list and print set (danh mục bản vẽ, print set) |

A card says: what to do (commands), **done when**, **skip only if**, never.

