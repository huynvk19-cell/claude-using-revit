---
name: drafting-views-sheets
description: "Viewports, viewport types, crops/scope boxes, dependent views, 2D grid/level ends, view templates and link display on sheets via Revit MCP. Use for viewport, đổi type viewport, crop view, scope box, đầu trục, kéo trục, bubble chồng, view template, ẩn link, V/G."
---

# Views and sheets

Standards: `~/.claude/drafting-domain/drafting-views-sheets.md`.

1. **Identify the views first.**
   - Use `sheet_browser_names` / `sheet_views_ids`.
   - Report names as the Project Browser shows them.
2. **Viewport type:** `set_viewport_type`.
   - Run preview, then apply.
   - It can undo from its log.
3. **Crop:** `view_crop_info`, then `crop_side_to_grids`.
   - A Scope Box attached to the view locks the crop. Report it as manual.
4. **Grid ends:** 2D only.
   - `align_grid_ends` lines them up.
   - Overlapping bubbles → stagger one 2D end. `grid_bubble_elbow` often fails.
   - Pull grids out only when asked, and move the grid dims with them.
5. **Hiding:** edit the controlling View Template.
   - Name the other views that share it.
   - Preview with `view_image_trial`.
6. **Link display in Revit 2023:** there is no API. Use UI automation, and only when the user asks.
7. **Afterwards:** re-place viewport titles on the affected sheets (`drafting-tags-titles`).
